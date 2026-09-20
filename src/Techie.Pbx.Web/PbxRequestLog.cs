using log4net;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Diagnostics;

namespace Techie.Pbx.Web
{
    /// <summary>
    /// Whether this process is writing a W3C request log, and where (D116). Decided once at
    /// startup, the same way <see cref="PbxSounds"/> decides where announcement audio lives: the
    /// answer depends on a setting and on where the app is installed, and neither changes under a
    /// running process.
    ///
    /// The setting is read once on purpose. Kestrel's W3C logger is middleware, so "off" means it
    /// is never added to the pipeline at all rather than added and asked to do nothing — which is
    /// why turning the log on or off needs the service restarted, and why the settings page says so.
    /// </summary>
    public static class PbxRequestLog
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(PbxRequestLog));

        /// <summary>Where the files are written, absolute. Empty until <see cref="Open"/> has run.</summary>
        public static string DirectoryPath { get; private set; } = "";

        /// <summary>Whether this process is logging requests at all.</summary>
        public static bool Enabled { get; private set; }

        /// <summary>
        /// The file being written right now, for the Logs page to tail, or null when the log is off
        /// or nothing has been written yet.
        /// </summary>
        public static string? Newest() =>
            Enabled ? RequestLog.Newest(DirectoryPath) : null;

        /// <summary>
        /// Reads the setting and works out the directory. The directory is created here rather than
        /// left to the logger, so a box that cannot write there says so in the application log at
        /// startup instead of silently keeping no request log — and a failure to create it turns
        /// the log off rather than stopping the app: an appliance that will not start is worse than
        /// one that starts without a diagnostic aid and says so.
        /// </summary>
        public static void Open(string contentRootPath, IReadOnlyDictionary<string, string> settings)
        {
            DirectoryPath = RequestLog.DirectoryIn(contentRootPath);

            if (!RequestLog.IsEnabled(settings))
            {
                Log.Info($"Web request logging is off ({SettingsKeys.WebRequestLog})");
                return;
            }

            try
            {
                Directory.CreateDirectory(DirectoryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Web request logging is off: {DirectoryPath} could not be created: {ex.Message}");
                return;
            }

            Enabled = true;
            Log.Info($"Web requests are logged in W3C format to {DirectoryPath}");
        }
    }
}
