using System.Globalization;
using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Pages.Settings;

namespace Techie.Pbx.Web.Pages.Parking
{
    /// <summary>
    /// The Parking page: call parking and the music a parked caller hears, in one place (D119).
    ///
    /// Two halves. The settings are a display shell, like the SIP and System pages: rows open the
    /// general settings page's edit form in the shared modal, so a setting is validated, written
    /// and logged in exactly one place however an admin got to it. The music on hold tracks are a
    /// list page like Announcements, and one save touches two things — a row and a file — in the
    /// order <see cref="OnPostSave"/> describes.
    /// </summary>
    [RequestSizeLimit(MohStore.MaxUploadBytes + (1024 * 1024))]
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly MohFileRepository mohFiles;
        private readonly SettingsRepository settings;
        private readonly MohStore store;

        public IndexModel()
        {
            this.mohFiles = new MohFileRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
            this.store = new MohStore();
        }

        public void OnGet()
        {
        }

        /// <summary>The create or edit form, which the page shows in the Bootstrap modal.</summary>
        public IActionResult OnGetForm(long? mohFileID)
        {
            if (mohFileID is null or 0)
                return this.Partial("_Form", new MohFileForm());

            var file = this.mohFiles.GetByID(mohFileID.Value);
            if (file == null)
                return this.NotFound();

            return this.Partial("_Form", this.Fill(new MohFileForm
            {
                MohFileID = file.MohFileID,
                Name = file.Name,
            }, file));
        }

        /// <summary>The parking settings, as the same grouped tables every settings page uses.</summary>
        public PartialViewResult OnGetSettings()
        {
            var stored = this.settings.GetAll();

            var sections = new List<SettingSection>
            {
                new()
                {
                    Help = "Press the feature code during a call to park it; the system speaks the slot number " +
                        "back to you. Dial that number from any phone to pick the call up. A call nobody claims " +
                        "rings back the phone that parked it when the timeout runs out.",
                    Rows = Rows(stored, SettingsKeys.ParkingEnabled, SettingsKeys.ParkingDtmfCode, SettingsKeys.ParkingSlots, SettingsKeys.ParkingTimeout),
                    Title = "Call parking",
                },
                new()
                {
                    Help = "What the parked caller hears while they wait. Music plays the tracks below, as one " +
                        "class, in the order they are listed.",
                    Rows = Rows(stored, SettingsKeys.ParkingAudio),
                    Title = "While they wait",
                },
            };

            return this.Partial("/Pages/Shared/_Sections.cshtml", sections);
        }

        /// <summary>
        /// The music on hold table, in the order Asterisk will play the tracks, plus whether
        /// anything is going to play them — which is the parking audio setting, so this refreshes
        /// when a setting changes as well as when a track does.
        /// </summary>
        public PartialViewResult OnGetTable()
        {
            var rows = this.mohFiles.GetAll()
                .Select(file =>
                {
                    var audio = this.store.Describe(file);

                    return new MohFileRow
                    {
                        Audio = Summarise(file, audio),
                        AudioUsable = audio != null,
                        File = file.HasAudio ? file.File : "—",
                        MohFileID = file.MohFileID,
                        Name = file.Name,
                    };
                })
                .ToList();

            return this.Partial("_Table", new MohTable
            {
                MusicSelected = AsteriskSettings.Parking(this.settings.GetAll()).UsesMusicOnHold,
                Rows = rows,
            });
        }

        /// <summary>
        /// Deletes the track and its file. The row goes first: a file left behind is music
        /// Asterisk would still play, whereas a row left behind is one line in a table.
        /// </summary>
        public IActionResult OnPostDelete(long mohFileID)
        {
            var file = this.mohFiles.GetByID(mohFileID);
            if (file == null)
                return this.NotFound();

            this.mohFiles.Delete(mohFileID);

            try
            {
                this.store.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The row is gone, but the file is not — and in this one directory that means
                // Asterisk would carry on playing it. Say so rather than claiming a clean delete.
                Log.Warn($"Music on hold track '{file.Name}' was deleted but its file could not be removed: {ex.Message}");
            }

            Log.Info($"Music on hold track '{file.Name}' deleted by {this.User.Identity?.Name}");
            return this.Changed($"Music on hold track '{file.Name}' deleted.");
        }

        /// <summary>
        /// Creates or renames one track, and replaces its audio when a file came with it.
        ///
        /// The order is the announcements page's (D55), for the same reasons. A new track has no
        /// ID until its row exists and the stored file name is derived from that ID, so the row is
        /// inserted first, the audio placed second, and the row updated with the file name it
        /// ended up with. A conversion ffmpeg refuses comes back as a message on the form with
        /// nothing on disk touched; a placement that fails leaves a track with no audio, which the
        /// table and the form both say plainly rather than pretending.
        /// </summary>
        public IActionResult OnPostSave(MohFileForm form)
        {
            var isNew = form.MohFileID == 0;
            var file = isNew ? new MohFile { CreatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() } : this.mohFiles.GetByID(form.MohFileID);
            if (file == null)
                return this.NotFound();

            var previousFile = file.File;
            file.Name = Text(form.Name);

            var uploaded = form.Audio is { Length: > 0 };

            // A track with no audio at all is not worth creating: nothing would play it, and there
            // is no other thing a music on hold row can be.
            if (isNew && !uploaded)
            {
                form.Errors = new List<string> { "Choose a file to upload. A music on hold track is the file." };
                return this.Partial("_Form", this.Fill(form, file));
            }

            try
            {
                // Either way the row comes back naming the file it should be stored as: the name
                // is derived from the ID and the track's name, so the repository owns it.
                if (isNew)
                    this.mohFiles.Insert(file);
                else
                    this.mohFiles.Update(file);
            }
            catch (ValidationFailedException ex)
            {
                file.File = previousFile;
                form.Errors = ex.Errors.ToList();
                return this.Partial("_Form", this.Fill(form, file));
            }

            try
            {
                if (uploaded)
                {
                    using var upload = form.Audio!.OpenReadStream();
                    this.store.Save(file, upload);
                }
                else if (previousFile.Length > 0)
                {
                    this.store.Rename(file.MohFileID, previousFile, file.File);
                }
            }
            catch (AudioUploadException ex)
            {
                Log.Warn($"Music on hold track '{file.Name}' saved, but its audio was refused: {ex.Message}");
                form.MohFileID = file.MohFileID;
                form.Errors = new List<string> { ex.Message };
                return this.Partial("_Form", this.Fill(form, file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Music on hold track '{file.Name}' saved, but its audio could not be written: {ex.Message}", ex);
                form.MohFileID = file.MohFileID;
                form.Errors = new List<string>
                {
                    $"The track was saved, but its audio could not be written to {this.store.MohPath}. " +
                    "Check that the directory exists and that the web user may write to it.",
                };
                return this.Partial("_Form", this.Fill(form, file));
            }

            Log.Info($"Music on hold track '{file.Name}' {(isNew ? "created" : "updated")} by {this.User.Identity?.Name}" +
                     (uploaded ? " with new audio" : ""));
            return this.Changed($"Music on hold track '{file.Name}' saved.");
        }

        /// <summary>One row per key, through the shared builder so a secret could never leak.</summary>
        private static List<SettingRow> Rows(IReadOnlyDictionary<string, string> stored, params string[] keys) =>
            keys.Select(key => SettingRow.For(SettingsCatalog.For(key), stored)).ToList();

        /// <summary>
        /// The audio as a line of text: how long it runs and how big it is, or why there is none.
        /// Read off the file rather than out of the database, so it cannot claim audio that is not
        /// there. Minutes rather than seconds, because hold music is measured in them.
        /// </summary>
        private static string Summarise(MohFile file, AnnouncementAudio? audio)
        {
            if (audio != null)
            {
                var minutes = audio.Seconds / 60;
                return $"{minutes.ToString("0.#", CultureInfo.InvariantCulture)} min, {audio.Bytes / 1024} KB";
            }

            return file.HasAudio ? "File missing" : "No audio";
        }

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to.
        /// "mohFilesChanged" refreshes the table, "configChanged" wakes the navbar's apply button
        /// (D43) — musiconhold.conf is generated from these rows — and "pbxToast" says what
        /// happened.
        /// </summary>
        private IActionResult Changed(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["mohFilesChanged"] = null,
                ["configChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// What the form cannot know for itself: whether there is audio on disk for this track and
        /// how it reads.
        /// </summary>
        private MohFileForm Fill(MohFileForm form, MohFile file)
        {
            var audio = file.MohFileID > 0 ? this.store.Describe(file) : null;

            form.AudioMissing = audio == null && file.HasAudio;
            form.AudioSummary = Summarise(file, audio);
            form.HasAudio = audio != null;

            return form;
        }
    }
}
