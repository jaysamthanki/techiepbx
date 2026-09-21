using System.Net.Sockets;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Asterisk.Status;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Pages.Status
{
    /// <summary>
    /// The home page: what this PBX is doing right now, and what about it needs attention. Built
    /// as a shell htmx fills, like every other page, but with two handlers on two clocks — the
    /// live half polls every five seconds, and the half that reads the database only comes back
    /// when something changed.
    ///
    /// The live half opens <b>one</b> AMI connection per poll and asks it four questions. Four
    /// connections would be four logins and four log lines every five seconds for as long as
    /// anybody has the page open (D21).
    /// </summary>
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly AnnouncementRepository announcements;
        private readonly PhoneButtonRepository buttons;
        private readonly CertificateRepository certificates;
        private readonly ExtensionRepository extensions;
        private readonly InboundRouteRepository inboundRoutes;
        private readonly IvrRepository ivrs;
        private readonly OutboundRouteRepository outboundRoutes;
        private readonly ConfigPendingMarker pending;
        private readonly PhoneRepository phones;
        private readonly AsteriskRestartMarker restart;
        private readonly RingGroupRepository ringGroups;
        private readonly SettingsRepository settings;
        private readonly TimeConditionRepository timeConditions;
        private readonly TrunkRepository trunks;

        public IndexModel()
        {
            this.announcements = new AnnouncementRepository(PbxDatabase.Current);
            this.buttons = new PhoneButtonRepository(PbxDatabase.Current);
            this.certificates = new CertificateRepository(PbxDatabase.Current);
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.inboundRoutes = new InboundRouteRepository(PbxDatabase.Current);
            this.ivrs = new IvrRepository(PbxDatabase.Current);
            this.outboundRoutes = new OutboundRouteRepository(PbxDatabase.Current);
            this.pending = new ConfigPendingMarker(PbxDatabase.Current);
            this.phones = new PhoneRepository(PbxDatabase.Current);
            this.restart = new AsteriskRestartMarker(PbxDatabase.Current);
            this.ringGroups = new RingGroupRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
            this.timeConditions = new TimeConditionRepository(PbxDatabase.Current);
            this.trunks = new TrunkRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>
        /// The half of the page that comes from the database: the findings, and the counts strip
        /// under them. Reading every table is more work than a five second poll should do, which
        /// is why this handler is on load and on change instead (D43's events).
        ///
        /// Only ever the partial, never a full page — the layout's own poll div would come with
        /// it and htmx would fetch the navbar's status inside this div forever
        /// (see ConfigStatusModel).
        /// </summary>
        public PartialViewResult OnGetAttention()
        {
            var settingValues = this.settings.GetAll();
            var trunkList = this.trunks.GetAll();
            var trunkStates = RegistrationStatus.ReadTrunks(AsteriskSettings.Ami(settingValues), trunkList.Select(t => t.Name));

            var snapshot = new StatusSnapshot
            {
                // ReadTrunks answers Unknown for every trunk when it could not ask, which is how
                // this handler knows AMI is down without opening a connection of its own. A box
                // with no trunks at all leaves nothing to infer from, and the live tiles are then
                // the only thing that says so.
                AmiReachable = trunkStates.Count == 0 || trunkStates.Values.Any(state => state != RegistrationState.Unknown),
                Announcements = this.announcements.GetAll(),
                Certificates = this.certificates.GetAll(),
                ConfigPending = this.pending.IsPending,
                Disks = Disks(),
                Extensions = this.extensions.GetAll(),
                InboundRoutes = this.inboundRoutes.GetAll(),
                Ivrs = this.ivrs.GetAll(),
                OutboundRoutes = this.outboundRoutes.GetAll(),
                // The line keys only: they are what say which extension a phone registers as
                // (schema 020), and nothing on this page asks about a lamp.
                PhoneButtons = this.buttons.GetLines(),
                Phones = this.phones.GetAll(),
                RestartRequired = this.restart.IsPending,
                RingGroups = this.ringGroups.GetAll(),
                TimeConditions = this.timeConditions.GetAll(),

                // The setting as stored, not AsteriskSettings.Timezone: the rule is about nobody
                // having chosen a zone, and that helper answers with the default instead (D74).
                Timezone = settingValues.TryGetValue(SettingsKeys.SystemTimezone, out var zone) ? zone : "",
                TrunkStates = trunkStates,
                Trunks = trunkList,
            };

            return this.Partial("_Attention", new AttentionView
            {
                Counts = this.Counts(snapshot),
                Findings = AttentionRules.Evaluate(snapshot, DateTimeOffset.UtcNow),
            });
        }

        /// <summary>
        /// The five second poll: one AMI conversation, four questions, and the tiles and call
        /// table that come out of it. It never throws — a page that redraws every five seconds
        /// cannot afford to break because Asterisk is restarting, so an unreachable AMI is a
        /// state this renders rather than an error (the same rule RegistrationStatus.Read follows).
        /// </summary>
        public PartialViewResult OnGetLive()
        {
            var enabledExtensions = this.extensions.GetAll().Where(e => e.Enabled).Select(e => e.Number).ToList();
            var registeringTrunks = this.trunks.GetAll().Where(t => t.Enabled && t.Register).Select(t => t.Name).ToList();
            var current = this.certificates.Current(DateTimeOffset.UtcNow);

            var live = new LiveStatus
            {
                CertificateDays = current?.DaysUntilExpiry(DateTimeOffset.UtcNow),
                ConfigPending = this.pending.IsPending,
                ExtensionsTotal = enabledExtensions.Count,
                RestartRequired = this.restart.IsPending,
                TrunksTotal = registeringTrunks.Count,
            };

            try
            {
                using var client = new AmiClient(AsteriskSettings.Ami(this.settings.GetAll()));
                var session = client.Connect();

                var contacts = session.ShowContacts();
                var registrations = session.ShowRegistrations();
                var channels = session.ShowChannels();
                var core = session.CoreStatus();

                var extensionStates = RegistrationStatus.Map(contacts, enabledExtensions);
                var trunkStates = RegistrationStatus.MapTrunks(registrations, registeringTrunks);

                live.ActiveCalls = ActiveCalls.FromChannels(channels);
                live.ExtensionsRegistered = extensionStates.Values.Count(state => state == RegistrationState.Registered);
                live.Reachable = true;
                live.StartedUtc = core.StartedUtc;
                live.TrunksRegistered = trunkStates.Values.Count(state => state == RegistrationState.Registered);
                live.TrunksRejected = trunkStates.Values.Any(state => state == RegistrationState.Rejected);
            }
            catch (Exception ex) when (ex is AmiException or IOException or SocketException)
            {
                Log.Warn($"Could not read live status over AMI: {ex.Message}");
            }

            return this.Partial("_Live", live);
        }

        /// <summary>
        /// How much of everything there is, each linking to its own page. Every row is counted,
        /// switched on or not: this is the size of the system, not what is running.
        /// </summary>
        private List<CountLink> Counts(StatusSnapshot snapshot) => new()
        {
            new CountLink
            {
                Count = snapshot.Extensions.Count,
                Plural = "extensions",
                Singular = "extension",
                Url = this.Url.Page("/Extensions/Index") ?? "",
            },
            new CountLink
            {
                Count = snapshot.Trunks.Count,
                Plural = "trunks",
                Singular = "trunk",
                Url = this.Url.Page("/Trunks/Index") ?? "",
            },
            new CountLink
            {
                Count = snapshot.Phones.Count,
                Plural = "phones",
                Singular = "phone",
                Url = this.Url.Page("/Phones/Index") ?? "",
            },
            new CountLink
            {
                Count = snapshot.InboundRoutes.Count,
                Plural = "inbound routes",
                Singular = "inbound route",
                Url = this.Url.Page("/Inbound/Index") ?? "",
            },
            new CountLink
            {
                Count = snapshot.RingGroups.Count,
                Plural = "ring groups",
                Singular = "ring group",
                Url = this.Url.Page("/RingGroups/Index") ?? "",
            },
            new CountLink
            {
                Count = snapshot.Ivrs.Count,
                Plural = "IVRs",
                Singular = "IVR",
                Url = this.Url.Page("/Ivrs/Index") ?? "",
            },
            new CountLink
            {
                Count = snapshot.TimeConditions.Count,
                Plural = "time conditions",
                Singular = "time condition",
                Url = this.Url.Page("/TimeConditions/Index") ?? "",
            },
            new CountLink
            {
                Count = snapshot.Announcements.Count,
                Plural = "announcements",
                Singular = "announcement",
                Url = this.Url.Page("/Announcements/Index") ?? "",
            },
        };

        /// <summary>The directories worth measuring: where the database is, and where audio goes.</summary>
        private static List<string> DiskPaths()
        {
            var paths = new List<string>();

            try
            {
                paths.Add(Path.GetDirectoryName(Path.GetFullPath(PbxDatabase.Current.FilePath))!);

                var sounds = PbxSounds.Current.SoundsPath;
                if (Directory.Exists(sounds))
                    paths.Add(sounds);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Log.Warn($"Could not work out which disks to measure: {ex.Message}");
            }

            return paths;
        }

        /// <summary>
        /// How full the disks this application writes to are: the one the database and its marker
        /// files live on, and the one announcement audio goes to. Two paths rather than every
        /// mount on the box, because these are the two that filling up would stop working.
        ///
        /// Nothing here is allowed to fail the page. A path that does not exist, a drive that is
        /// not ready, a container with no such mount: all of them mean "no answer", which is a
        /// status page with one fewer rule to check.
        /// </summary>
        private static List<DiskUsage> Disks()
        {
            var disks = new List<DiskUsage>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in DiskPaths())
            {
                var drive = DriveFor(path);

                // Both paths are usually on the same filesystem, and saying so twice would be two
                // findings about one disk.
                if (drive == null || !seen.Add(drive.Name))
                    continue;

                var used = drive.TotalSize - drive.TotalFreeSpace;

                disks.Add(new DiskUsage
                {
                    Path = path,
                    UsedPercent = (int)Math.Round(used * 100.0 / drive.TotalSize),
                });
            }

            return disks;
        }

        /// <summary>
        /// The filesystem a path is on: the mounted drive whose root is the longest start of it.
        /// On Debian /var may well be a filesystem of its own, and answering about "/" would then
        /// be answering about the wrong disk.
        /// </summary>
        private static DriveInfo? DriveFor(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);

                return DriveInfo.GetDrives()
                    .Where(drive => drive.IsReady && drive.TotalSize > 0 &&
                        full.StartsWith(drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(drive => drive.RootDirectory.FullName.Length)
                    .FirstOrDefault();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Log.Warn($"Could not measure the disk holding {path}: {ex.Message}");
                return null;
            }
        }
    }
}
