using log4net;
using log4net.Appender;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Diagnostics;

namespace Techie.Pbx.Web.Pages.Status
{
    /// <summary>
    /// Exactly the logs the Logs page is allowed to show. The browser never sends a path — only
    /// one of these <see cref="LogSource.Name"/>s — and <see cref="Find"/> is the only way a name
    /// turns into a file, so there is nowhere for a path to sneak in from the request. Adding a
    /// source means adding an entry here, which is the point: the list is the allowlist.
    /// </summary>
    public static class LogSources
    {
        /// <summary>Every source, in the order the dropdown lists them.</summary>
        public static readonly IReadOnlyList<LogSource> All = new List<LogSource>
        {
            new LogSource
            {
                Name = "asterisk-messages",
                Label = "Asterisk messages",
                Resolve = settings => Path.Combine(LogDirectory(settings), "messages.log"),
            },
            new LogSource
            {
                Name = "asterisk-security",
                Label = "Asterisk security",
                Resolve = settings => Path.Combine(LogDirectory(settings), LoggerConfRenderer.SecurityLogFile),
            },
            new LogSource
            {
                Name = "app",
                Label = "TNPBX application",
                NoFileMessage = "The application is not logging to a file.",
                Resolve = _ => AppLogFile(),
            },
            new LogSource
            {
                Name = "requests",
                Label = "Web requests (W3C)",
                NoFileMessage = "There is no request log. It is written only while the " +
                    SettingsKeys.WebRequestLog + " setting is on, and that setting is read when the " +
                    "service starts — so turn it on, then restart tnpbx-web.",
                Resolve = _ => PbxRequestLog.Newest(),
            },
        };

        /// <summary>The entry named <paramref name="name"/>, or null when it is not one of <see cref="All"/>.</summary>
        public static LogSource? Find(string? name) =>
            All.FirstOrDefault(source => source.Name == name);

        /// <summary>
        /// The application's own log file, read from the live log4net configuration rather than
        /// from log4net.config directly, so this can never name a path other than the one log4net
        /// is actually writing to. Null when the root logger has no file appender at all.
        /// </summary>
        private static string? AppLogFile() => LogManager.GetRepository()
            .GetAppenders()
            .OfType<FileAppender>()
            .Select(appender => appender.File)
            .FirstOrDefault();

        /// <summary>Asterisk.LogDirectory, or Asterisk's own default when nobody has set it.</summary>
        private static string LogDirectory(IReadOnlyDictionary<string, string> settings) =>
            settings.TryGetValue(SettingsKeys.AsteriskLogDirectory, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : LogTail.DefaultLogDirectory;
    }
}
