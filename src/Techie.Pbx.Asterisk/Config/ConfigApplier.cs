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

        private readonly string _confDirectory;
        private readonly PjsipTransport _transport;
        private readonly ExtensionRepository _extensions;
        private readonly AmiSettings _ami;

        public ConfigApplier(string confDirectory, PjsipTransport transport, ExtensionRepository extensions, AmiSettings ami)
        {
            _confDirectory = confDirectory;
            _transport = transport;
            _extensions = extensions;
            _ami = ami;
        }

        /// <summary>
        /// The normal wiring: conf directory, SIP transport and AMI credentials all come out of
        /// the Settings table, extensions out of theirs. Settings are read once per apply, so a
        /// change made in the UI is picked up by the next apply without a restart.
        /// </summary>
        public static ConfigApplier FromDatabase(SettingsRepository settings, ExtensionRepository extensions)
        {
            var values = settings.GetAll();

            return new ConfigApplier(
                AsteriskSettings.ConfDirectory(values),
                AsteriskSettings.Transport(values),
                extensions,
                AsteriskSettings.Ami(values));
        }

        /// <summary>
        /// Loads every row and renders every generated file. Reads the database, writes nothing.
        /// </summary>
        public List<GeneratedFile> Render()
        {
            var extensions = _extensions.GetAll();

            return new List<GeneratedFile>
            {
                new("pjsip.conf", PjsipModule, PjsipConfRenderer.Render(_transport, extensions)),
                new("extensions.conf", DialplanModule, ExtensionsConfRenderer.Render(extensions)),
            };
        }

        /// <summary>
        /// Renders and writes every file, and returns only the ones whose content actually
        /// changed. Nothing is reloaded: use Apply for that.
        /// </summary>
        public List<GeneratedFile> Write()
        {
            var changed = new List<GeneratedFile>();

            foreach (var file in Render())
            {
                if (ConfFileWriter.WriteAtomic(_confDirectory, file.FileName, file.Content))
                    changed.Add(file);
            }

            return changed;
        }

        /// <summary>
        /// Render, write, then reload the modules whose files changed. When nothing changed, no
        /// AMI connection is opened at all.
        /// </summary>
        public ApplyResult Apply()
        {
            var changed = Write();

            if (changed.Count == 0)
            {
                Log.Info("Apply config: every file was already up to date, nothing reloaded");
                return new ApplyResult(new List<string>(), new List<string>());
            }

            var files = changed.Select(f => f.FileName).ToList();
            var modules = changed.Select(f => f.Module).Distinct(StringComparer.Ordinal).ToList();

            using var client = new AmiClient(_ami);
            var session = client.Connect();

            foreach (var module in modules)
                session.Reload(module);

            Log.Info($"Apply config: wrote {string.Join(", ", files)}, reloaded {string.Join(", ", modules)}");
            return new ApplyResult(files, modules);
        }
    }
}
