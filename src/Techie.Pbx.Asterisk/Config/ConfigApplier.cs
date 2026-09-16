using log4net;
using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Turns the database into running config: load every row, render every file, write the ones
    /// that changed, then reload only the Asterisk modules those files belong to. Files Asterisk
    /// only reads at startup are written too, and reported as needing a restart (D33).
    /// </summary>
    public class ConfigApplier
    {
        /// <summary>Owns pjsip.conf. Reloading it re-reads every PJSIP object.</summary>
        public const string PjsipModule = "res_pjsip";

        /// <summary>Owns extensions.conf, i.e. what "dialplan reload" reloads.</summary>
        public const string DialplanModule = "pbx_config";

        /// <summary>Owns voicemail.conf.</summary>
        public const string VoicemailModule = "app_voicemail";

        /// <summary>
        /// logger.conf. Not a .so: the logger is part of the core, and "logger" is one of the
        /// reload classes Asterisk accepts alongside real module names.
        /// </summary>
        public const string LoggerModule = "logger";

        /// <summary>
        /// manager.conf. Core as well, for the same reason. Reloading it can drop the very
        /// connection we are reloading from, which is expected rather than a failure (D34).
        /// </summary>
        public const string ManagerModule = "manager";

        private static readonly ILog Log = LogManager.GetLogger(typeof(ConfigApplier));

        private readonly string confDirectory;
        private readonly PjsipTransport transport;
        private readonly ExtensionRepository extensions;
        private readonly TrunkRepository trunks;
        private readonly AmiSettings ami;
        private readonly ConfigPendingMarker pending;

        public ConfigApplier(
            string confDirectory,
            PjsipTransport transport,
            ExtensionRepository extensions,
            TrunkRepository trunks,
            AmiSettings ami,
            ConfigPendingMarker pending)
        {
            this.confDirectory = confDirectory;
            this.transport = transport;
            this.extensions = extensions;
            this.trunks = trunks;
            this.ami = ami;
            this.pending = pending;
        }

        /// <summary>
        /// The normal wiring: conf directory, SIP transport and AMI credentials all come out of
        /// the Settings table, extensions out of theirs, and the "apply is due" marker out of the
        /// data folder next to the database. Settings are read once per apply, so a change made in
        /// the UI is picked up by the next apply without a restart.
        /// </summary>
        public static ConfigApplier FromDatabase(
            Database database,
            SettingsRepository settings,
            ExtensionRepository extensions,
            TrunkRepository trunks)
        {
            var values = settings.GetAll();

            return new ConfigApplier(
                AsteriskSettings.ConfDirectory(values),
                AsteriskSettings.Transport(values),
                extensions,
                trunks,
                AsteriskSettings.Ami(values),
                new ConfigPendingMarker(database));
        }

        /// <summary>
        /// Render, write, then reload the modules whose files changed. When nothing changed, no
        /// AMI connection is opened at all. Either way the config on disk now matches the
        /// database, so the "apply is due" marker goes, even when a restart is still owed.
        /// </summary>
        public ApplyResult Apply()
        {
            var changed = this.Write();

            if (changed.Count == 0)
            {
                Log.Info("Apply config: every file was already up to date, nothing reloaded");
                this.pending.Clear();
                return new ApplyResult(new List<string>(), new List<string>(), new List<string>());
            }

            var files = changed.Select(f => f.FileName).ToList();
            var restartFiles = changed.Where(f => f.NeedsRestart).Select(f => f.FileName).ToList();
            var modules = ReloadOrder(changed);

            if (modules.Count > 0)
            {
                using var client = new AmiClient(this.ami);
                var session = client.Connect();

                foreach (var module in modules)
                    this.Reload(session, module);
            }

            // Only once the reloads worked: a failure leaves the marker up, and it should.
            this.pending.Clear();

            Log.Info($"Apply config: wrote {string.Join(", ", files)}, reloaded {Listed(modules)}");
            if (restartFiles.Count > 0)
                Log.Warn($"Apply config: {string.Join(", ", restartFiles)} need an Asterisk restart to take effect");

            return new ApplyResult(files, modules, restartFiles);
        }

        /// <summary>
        /// Loads every row and renders every generated file. Reads the database, writes nothing.
        /// After this piece, everything in the conf directory is on this list: nothing in
        /// /etc/asterisk is hand written any more.
        /// </summary>
        public List<GeneratedFile> Render()
        {
            var all = this.extensions.GetAll();
            var allTrunks = this.trunks.GetAll();

            return new List<GeneratedFile>
            {
                // Read once at startup: written here, applied by a restart (D33).
                new("asterisk.conf", null, AsteriskConfRenderer.Render()),
                new("modules.conf", null, ModulesConfRenderer.Render()),
                new("rtp.conf", null, RtpConfRenderer.Render()),

                new("logger.conf", LoggerModule, LoggerConfRenderer.Render()),
                new("manager.conf", ManagerModule, ManagerConfRenderer.Render(this.ami)),
                new("pjsip.conf", PjsipModule, PjsipConfRenderer.Render(this.transport, all, allTrunks)),
                new("extensions.conf", DialplanModule, ExtensionsConfRenderer.Render(all, allTrunks)),
                new("voicemail.conf", VoicemailModule, VoicemailConfRenderer.Render(all)),
            };
        }

        /// <summary>
        /// Renders and writes every file, and returns only the ones whose content actually
        /// changed. Nothing is reloaded and the marker is left alone: use Apply for that.
        /// </summary>
        public List<GeneratedFile> Write()
        {
            var changed = new List<GeneratedFile>();

            foreach (var file in this.Render())
            {
                if (ConfFileWriter.WriteAtomic(this.confDirectory, file.FileName, file.Content))
                    changed.Add(file);
            }

            return changed;
        }

        /// <summary>
        /// The reload plan for a set of changed files: each module once, in the order they will be
        /// reloaded, with manager last because reloading manager.conf can close the AMI session we
        /// are giving the orders on (D34). Files that need a restart are not in it.
        /// </summary>
        public static List<string> ReloadOrder(List<GeneratedFile> changed)
        {
            return changed
                .Where(f => !f.NeedsRestart)
                .Select(f => f.Module!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(module => string.Equals(module, ManagerModule, StringComparison.Ordinal) ? 1 : 0)
                .ToList();
        }

        private static string Listed(List<string> modules) => modules.Count == 0 ? "nothing" : string.Join(", ", modules);

        /// <summary>
        /// Reloading manager.conf makes Asterisk rebuild its AMI sessions, and ours is one of
        /// them: it can answer and then hang up, or hang up without answering. Either way the
        /// reload happened, so losing the connection here is not a failed apply. Every other
        /// module's reload still has to succeed.
        /// </summary>
        private void Reload(AmiSession session, string module)
        {
            if (!string.Equals(module, ManagerModule, StringComparison.Ordinal))
            {
                session.Reload(module);
                return;
            }

            try
            {
                session.Reload(module);
            }
            catch (Exception ex) when (ex is AmiException or IOException)
            {
                Log.Info($"AMI closed while reloading '{module}', which is what reloading manager.conf does: {ex.Message}");
            }
        }
    }
}
