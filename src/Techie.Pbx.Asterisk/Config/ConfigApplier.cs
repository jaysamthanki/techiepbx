using log4net;
using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Turns the database into running config: load every row, render every file, write the ones
    /// that changed, then reload only the Asterisk modules those files belong to.
    /// </summary>
    public class ConfigApplier
    {
        /// <summary>Owns pjsip.conf. Reloading it re-reads every PJSIP object.</summary>
        public const string PjsipModule = "res_pjsip";

        /// <summary>Owns extensions.conf, i.e. what "dialplan reload" reloads.</summary>
        public const string DialplanModule = "pbx_config";

        private static readonly ILog Log = LogManager.GetLogger(typeof(ConfigApplier));

        private readonly string confDirectory;
        private readonly PjsipTransport transport;
        private readonly ExtensionRepository extensions;
        private readonly AmiSettings ami;
        private readonly ConfigPendingMarker pending;

        public ConfigApplier(
            string confDirectory,
            PjsipTransport transport,
            ExtensionRepository extensions,
            AmiSettings ami,
            ConfigPendingMarker pending)
        {
            this.confDirectory = confDirectory;
            this.transport = transport;
            this.extensions = extensions;
            this.ami = ami;
            this.pending = pending;
        }

        /// <summary>
        /// The normal wiring: conf directory, SIP transport and AMI credentials all come out of
        /// the Settings table, extensions out of theirs, and the "apply is due" marker out of the
        /// data folder next to the database. Settings are read once per apply, so a change made in
        /// the UI is picked up by the next apply without a restart.
        /// </summary>
        public static ConfigApplier FromDatabase(Database database, SettingsRepository settings, ExtensionRepository extensions)
        {
            var values = settings.GetAll();

            return new ConfigApplier(
                AsteriskSettings.ConfDirectory(values),
                AsteriskSettings.Transport(values),
                extensions,
                AsteriskSettings.Ami(values),
                new ConfigPendingMarker(database));
        }

        /// <summary>
        /// Render, write, then reload the modules whose files changed. When nothing changed, no
        /// AMI connection is opened at all. Either way the config now matches the database, so the
        /// "apply is due" marker goes.
        /// </summary>
        public ApplyResult Apply()
        {
            var changed = this.Write();

            if (changed.Count == 0)
            {
                Log.Info("Apply config: every file was already up to date, nothing reloaded");
                this.pending.Clear();
                return new ApplyResult(new List<string>(), new List<string>());
            }

            var files = changed.Select(f => f.FileName).ToList();
            var modules = changed.Select(f => f.Module).Distinct(StringComparer.Ordinal).ToList();

            using var client = new AmiClient(this.ami);
            var session = client.Connect();

            foreach (var module in modules)
                session.Reload(module);

            // Only once the reload worked: a failure leaves the marker up, and it should.
            this.pending.Clear();

            Log.Info($"Apply config: wrote {string.Join(", ", files)}, reloaded {string.Join(", ", modules)}");
            return new ApplyResult(files, modules);
        }

        /// <summary>
        /// Loads every row and renders every generated file. Reads the database, writes nothing.
        /// </summary>
        public List<GeneratedFile> Render()
        {
            var all = this.extensions.GetAll();

            return new List<GeneratedFile>
            {
                new("pjsip.conf", PjsipModule, PjsipConfRenderer.Render(this.transport, all)),
                new("extensions.conf", DialplanModule, ExtensionsConfRenderer.Render(all)),
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
    }
}
