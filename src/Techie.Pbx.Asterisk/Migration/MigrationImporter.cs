using log4net;
using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Migration;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Migration
{
    /// <summary>
    /// Writes an <see cref="ImportPlan"/> (D170): every row through its own repository, so every
    /// rule a row typed into a form meets is met here too, in the order things depend on each
    /// other — extensions, trunks, outbound routes, inbound routes, sounds, phones, voicemail.
    ///
    /// A row the repository refuses becomes a warning and the import carries on: the plan was
    /// checked when it was previewed, so a refusal now means the database changed in between, and
    /// a migration that half-lands and says why beats one that stops. Nothing that depends on a
    /// refused row is written — a route whose trunk did not land, a key on an extension that did
    /// not, a message for a mailbox that did not.
    ///
    /// This does not apply the config. The page that runs it applies afterwards, the same way
    /// the navbar's button does.
    /// </summary>
    public class MigrationImporter
    {
        /// <summary>Owner and group read/write: app_voicemail moves and deletes these as the asterisk group.</summary>
        private const UnixFileMode MessageFileMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite;

        /// <summary>The same plus enter, plus setgid so what is created under it keeps the directory's group (D18).</summary>
        private const UnixFileMode MessageDirectoryMode =
            UnixFileMode.SetGroup |
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute;

        private static readonly ILog Log = LogManager.GetLogger(typeof(MigrationImporter));

        private readonly AnnouncementRepository announcements;
        private readonly PhoneButtonRepository buttons;

        /// <summary>Kept for the clear-first snapshot <see cref="ExistingConfig.FromDatabase"/> reads (D174).</summary>
        private readonly Database database;
        private readonly ExtensionRepository extensions;
        private readonly InboundRouteRepository inbound;
        private readonly OutboundRouteRepository outbound;
        private readonly PhoneRepository phones;
        private readonly AnnouncementStore sounds;
        private readonly string spoolRoot;
        private readonly TrunkRepository trunks;

        /// <param name="spoolRoot">
        /// The voicemail spool, <see cref="VoicemailSpool.Root"/> on a real install; a parameter so
        /// the tests can point it at a temporary folder.
        /// </param>
        public MigrationImporter(Database database, AnnouncementStore sounds, string spoolRoot)
        {
            this.announcements = new AnnouncementRepository(database);
            this.database = database;
            this.buttons = new PhoneButtonRepository(database);
            this.extensions = new ExtensionRepository(database);
            this.inbound = new InboundRouteRepository(database);
            this.outbound = new OutboundRouteRepository(database);
            this.phones = new PhoneRepository(database);
            this.sounds = sounds;
            this.spoolRoot = spoolRoot;
            this.trunks = new TrunkRepository(database);
        }

        /// <summary>Writes the plan. Returns what landed and every warning, the plan's first.</summary>
        public ImportReport Run(ImportPlan plan)
        {
            var report = new ImportReport { Warnings = new List<MigrationWarning>(plan.Warnings) };

            this.ClearExisting(report);
            this.ImportExtensions(plan, report);
            var trunkIDs = this.ImportTrunks(plan, report);
            this.ImportOutboundRoutes(plan, report, trunkIDs);
            this.ImportInboundRoutes(plan, report, trunkIDs);
            this.ImportSounds(plan, report);
            this.ImportPhones(plan, report);
            this.ImportVoicemail(plan, report);

            Log.Info($"FreePBX import: cleared {report.Cleared.ExtensionNumbers.Count} extension(s), " +
                     $"{report.Cleared.TrunkNames.Count} trunk(s), {report.Cleared.PhoneMacs.Count} phone(s), " +
                     $"{report.Cleared.OutboundRouteNames.Count} outbound route(s), " +
                     $"{report.Cleared.InboundRouteNames.Count} inbound route(s), " +
                     $"{report.Cleared.AnnouncementNames.Count} announcement(s); then {report.Extensions.Count} " +
                     $"extensions, {report.Trunks.Count} trunks (disabled), {report.OutboundRoutes.Count} outbound " +
                     $"routes, {report.InboundRoutes.Count} inbound routes, {report.Announcements.Count} " +
                     $"announcements, {report.Phones.Count} phones, {report.VoicemailMessages} voicemail messages, " +
                     $"{report.Warnings.Count} warnings");

            return report;
        }

        /// <summary>
        /// The import clears first (D174): extensions, trunks, phones with their keys, routes and
        /// announcements are the import's tables, and everything already in them goes before
        /// anything lands — no keep-or-overwrite decisions, the import is the whole truth once it
        /// finishes. Settings, users and everything else it does not own are untouched, and the
        /// voicemail spool of a cleared extension is left alone: messages are copied, never deleted.
        /// </summary>
        private void ClearExisting(ImportReport report)
        {
            report.Cleared = ExistingConfig.FromDatabase(this.database);

            foreach (var phone in this.phones.GetAll())
                this.buttons.DeleteForPhone(phone.PhoneID);

            foreach (var route in this.outbound.GetAll())
                this.outbound.Delete(route.OutboundRouteID);

            foreach (var route in this.inbound.GetAll())
                this.inbound.Delete(route.InboundRouteID);

            foreach (var trunk in this.trunks.GetAll())
                this.trunks.Delete(trunk.TrunkID);

            foreach (var announcement in this.announcements.GetAll())
            {
                try
                {
                    this.announcements.Delete(announcement.AnnouncementID);
                }
                catch (ValidationFailedException)
                {
                    // An IVR greets with it (D58) and the import does not own IVRs: leave the row
                    // and say so, rather than breaking the menu. The import's announcement of the
                    // same name will not land either, and that warning says the rest.
                    report.Cleared.AnnouncementNames.Remove(announcement.Name);
                    Warn(report, MigrationSection.Announcements, $"Announcement '{announcement.Name}' was not cleared.",
                        "An IVR still greets with it, and the import does not own IVRs.",
                        "The imported announcement of that name will not land either. Point the IVR at another announcement and delete this one by hand if it should be replaced.");
                }
            }

            foreach (var extension in this.extensions.GetAll())
                this.extensions.Delete(extension.ExtensionID);

            foreach (var phone in this.phones.GetAll())
                this.phones.Delete(phone.PhoneID);
        }

        /// <summary>One message, folder by folder, never over a message that is already there.</summary>
        private static int CopyMailbox(PlannedMailbox mailbox, string target)
        {
            var copied = 0;

            foreach (var folder in mailbox.Files.Select(f => f.Split('/')[0]).Distinct(StringComparer.Ordinal))
            {
                var directory = Path.Combine(target, folder);

                if (Directory.Exists(directory) && Directory.EnumerateFiles(directory, "msg*").Any())
                    throw new IOException($"{directory} already holds messages, and imported ones would be numbered over them.");
            }

            CreateDirectory(target);

            foreach (var file in mailbox.Files)
            {
                var source = Path.Combine(mailbox.SourceDirectory, file);
                var destination = Path.Combine(target, file);

                CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
                SetMode(destination, MessageFileMode);

                if (file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    copied++;
            }

            return copied;
        }

        private static void CreateDirectory(string path)
        {
            if (Directory.Exists(path))
                return;

            Directory.CreateDirectory(path);
            SetMode(path, MessageDirectoryMode);
        }

        private void ImportExtensions(ImportPlan plan, ImportReport report)
        {
            foreach (var extension in plan.Extensions)
            {
                try
                {
                    this.extensions.Insert(extension);
                    report.Extensions.Add(extension.Number);
                }
                catch (ValidationFailedException ex)
                {
                    Warn(report, MigrationSection.Extensions, $"Extension {extension.Number} was not imported.",
                        string.Join(" ", ex.Errors), "Create it by hand on the Extensions page.");
                }
            }
        }

        private void ImportInboundRoutes(ImportPlan plan, ImportReport report, Dictionary<string, long> trunkIDs)
        {
            foreach (var planned in plan.InboundRoutes)
            {
                var route = planned.Route;
                var label = route.CatchAll ? $"The catch-all route on {planned.TrunkName}" : $"The route for DID {route.DID} on {planned.TrunkName}";

                if (!trunkIDs.TryGetValue(planned.TrunkName, out var trunkID))
                {
                    Warn(report, MigrationSection.InboundRoutes, $"{label} was not imported.",
                        $"Its trunk {planned.TrunkName} did not land.", "Add the trunk, then the route, by hand.");
                    continue;
                }

                route.TrunkID = trunkID;

                try
                {
                    this.inbound.Insert(route, allowDisabledTrunk: true);
                    report.InboundRoutes.Add(route.CatchAll ? $"Catch-all on {planned.TrunkName}" : $"DID {route.DID} on {planned.TrunkName}");
                }
                catch (ValidationFailedException ex)
                {
                    Warn(report, MigrationSection.InboundRoutes, $"{label} was not imported.",
                        string.Join(" ", ex.Errors), "Add it by hand on the Inbound routes page.");
                }
            }
        }

        private void ImportOutboundRoutes(ImportPlan plan, ImportReport report, Dictionary<string, long> trunkIDs)
        {
            foreach (var planned in plan.OutboundRoutes)
            {
                var route = planned.Route;

                if (!trunkIDs.TryGetValue(planned.TrunkName, out var trunkID))
                {
                    Warn(report, MigrationSection.OutboundRoutes, $"Outbound route {route.Name} was not imported.",
                        $"Its trunk {planned.TrunkName} did not land.", "Add the trunk, then the route, by hand.");
                    continue;
                }

                route.TrunkID = trunkID;

                try
                {
                    this.outbound.Insert(route, allowDisabledTrunk: true);
                    report.OutboundRoutes.Add(route.Name);
                }
                catch (ValidationFailedException ex)
                {
                    Warn(report, MigrationSection.OutboundRoutes, $"Outbound route {route.Name} was not imported.",
                        string.Join(" ", ex.Errors), "Add it by hand on the Routes page.");
                }
            }
        }

        /// <summary>The phone row, then its keys. Keys the repository refuses leave the phone with none.</summary>
        private void ImportPhones(ImportPlan plan, ImportReport report)
        {
            foreach (var planned in plan.Phones)
            {
                try
                {
                    this.phones.Insert(planned.Phone);
                    report.Phones.Add(planned.Phone.Mac);
                }
                catch (ValidationFailedException ex)
                {
                    Warn(report, MigrationSection.Phones, $"Phone {planned.Phone.Mac} was not imported.",
                        string.Join(" ", ex.Errors), "Nothing: a phone that provisions from TNPBX adds itself.");
                    continue;
                }

                if (planned.Buttons.Count == 0)
                    continue;

                try
                {
                    this.buttons.Replace(planned.Phone.PhoneID, planned.Buttons);
                }
                catch (ValidationFailedException ex)
                {
                    Warn(report, MigrationSection.Phones, $"Phone {planned.Phone.Mac} was imported without keys.",
                        string.Join(" ", ex.Errors), "Assign its keys on the Phones page.");
                }
            }
        }

        /// <summary>
        /// The announcement row first, with no audio; then the audio, converted the way an upload
        /// is; then the row is told the file it has. A failure leaves an announcement that says it
        /// has no audio, which the Announcements page shows, rather than one naming a file that
        /// is not there.
        /// </summary>
        private void ImportSounds(ImportPlan plan, ImportReport report)
        {
            foreach (var planned in plan.Sounds)
            {
                var announcement = planned.Announcement;

                try
                {
                    this.announcements.Insert(announcement);
                    report.Announcements.Add(announcement.Name);
                }
                catch (ValidationFailedException ex)
                {
                    Warn(report, MigrationSection.Sounds, $"Announcement '{announcement.Name}' was not imported.",
                        string.Join(" ", ex.Errors), "Upload it by hand on the Announcements page.");
                    continue;
                }

                if (!planned.Convertible)
                    continue;

                try
                {
                    announcement.AudioFile = this.StoreAudio(planned, report);
                    this.announcements.Update(announcement);
                }
                catch (AudioUploadException ex)
                {
                    Warn(report, MigrationSection.Sounds, $"Announcement '{announcement.Name}' was created with no audio.",
                        $"{planned.SourceFile} could not be stored: {ex.Message}",
                        "Upload the recording on the Announcements page.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Error($"Import: the audio for announcement '{announcement.Name}' could not be written: {ex.Message}", ex);
                    Warn(report, MigrationSection.Sounds, $"Announcement '{announcement.Name}' was created with no audio.",
                        $"The audio could not be written to {this.sounds.SoundsPath}: {ex.Message}",
                        "Check that the directory exists and the web user may write to it, then upload the recording on the Announcements page.");
                }
            }
        }

        /// <summary>Every trunk, disabled whatever the plan says (D170). Returns planned name to ID.</summary>
        private Dictionary<string, long> ImportTrunks(ImportPlan plan, ImportReport report)
        {
            var ids = new Dictionary<string, long>(StringComparer.Ordinal);

            foreach (var trunk in plan.Trunks)
            {
                // The plan already says so; this is the one line that makes it impossible not to.
                trunk.Enabled = false;

                try
                {
                    ids[trunk.Name] = this.trunks.Insert(trunk);
                    report.Trunks.Add(trunk.Name);
                }
                catch (ValidationFailedException ex)
                {
                    Warn(report, MigrationSection.Trunks, $"Trunk {trunk.Name} was not imported.",
                        string.Join(" ", ex.Errors), "Add it by hand on the Trunks page.");
                }
            }

            return ids;
        }

        /// <summary>
        /// The messages, into <c>&lt;spool&gt;/default/&lt;ext&gt;/INBOX|Old</c> with app_voicemail's own
        /// names. The web user is in the asterisk group, but the spool is Asterisk's: where it may
        /// not write, the mailbox is reported with the command that does it by hand, and the
        /// staging directory is kept so that command has something to copy.
        /// </summary>
        private void ImportVoicemail(ImportPlan plan, ImportReport report)
        {
            var landed = new HashSet<string>(report.Extensions, StringComparer.Ordinal);

            foreach (var mailbox in plan.Mailboxes)
            {
                if (!landed.Contains(mailbox.Mailbox))
                {
                    Warn(report, MigrationSection.Voicemail, $"{mailbox.Messages} voicemail message(s) for {mailbox.Mailbox} were not copied.",
                        $"Extension {mailbox.Mailbox} did not land.", "Create the extension, then copy the messages by hand.");
                    continue;
                }

                var target = Path.Combine(this.spoolRoot, VoicemailConfRenderer.MailboxContext, mailbox.Mailbox);

                try
                {
                    report.VoicemailMessages += CopyMailbox(mailbox, target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Warn($"Import: voicemail for {mailbox.Mailbox} could not be copied into {target}: {ex.Message}");
                    report.StagingKept = true;
                    Warn(report, MigrationSection.Voicemail, $"{mailbox.Messages} voicemail message(s) for {mailbox.Mailbox} were not copied.",
                        $"Writing into {target} failed: {ex.Message}",
                        $"As root: mkdir -p {target} && cp -an {mailbox.SourceDirectory}/. {target}/ && chown -R asterisk:asterisk {target}. " +
                        "The export is kept on the server until the next upload so this can be done.");
                }
            }
        }

        private static void SetMode(string path, UnixFileMode mode)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, mode);
        }

        /// <summary>
        /// Converts and stores, the way an upload is. If that fails — most often ffmpeg is not
        /// installed — a WAV is kept as it is and the operator told, because FreePBX's recordings
        /// are usually already the 8 kHz mono WAV Asterisk plays (D55).
        /// </summary>
        private string StoreAudio(PlannedSound planned, ImportReport report)
        {
            try
            {
                using var source = File.OpenRead(planned.SourcePath);
                return this.sounds.Save(planned.Announcement, source);
            }
            catch (AudioUploadException ex) when (Path.GetExtension(planned.SourcePath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = this.sounds.SaveUnconverted(planned.Announcement, planned.SourcePath);

                Warn(report, MigrationSection.Sounds, $"Announcement '{planned.Announcement.Name}' kept FreePBX's WAV unconverted.",
                    $"Converting it failed: {ex.Message}",
                    "Dial or play it to check. If it is silent or noise, install ffmpeg and upload it again on the Announcements page.");

                return fileName;
            }
        }

        private static void Warn(ImportReport report, string section, string what, string why, string action) =>
            report.Warnings.Add(new MigrationWarning(section, what, why, action));
    }
}
