using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Diagnostics
{
    /// <summary>
    /// Where the W3C web request log lives and whether it is being written at all (D116). The
    /// numbers and the naming rule are here, as pure functions, because two quite separate places
    /// need the same answers: startup, which configures Kestrel's W3C logger, and the Logs page,
    /// which has to find the file that logger is writing to right now.
    ///
    /// It writes nothing and reads no settings of its own — <see cref="LogTail"/> does the reading,
    /// exactly as it does for Asterisk's logs.
    /// </summary>
    public static class RequestLog
    {
        /// <summary>
        /// On unless somebody turns it off. A request log is only worth having if it was already
        /// running when the thing you are trying to explain happened: a phone that failed to fetch
        /// its config last night cannot be made to fail again on demand.
        /// </summary>
        public const bool EnabledByDefault = true;

        /// <summary>
        /// The prefix W3CLogger puts in front of every file it writes; it appends the date and a
        /// counter of its own. Ours rather than the default "w3clog-" so the file says whose it is
        /// when somebody finds it on the box.
        /// </summary>
        public const string FileNamePrefix = "tnpbx-requests-";

        /// <summary>One file's cap before the logger rolls to the next, in bytes.</summary>
        public const int FileSizeLimitBytes = 10 * 1024 * 1024;

        /// <summary>
        /// How long a line may sit in the logger's buffer before it reaches the disk. A second,
        /// because the Logs page polls every five and a log that lags behind the thing you are
        /// watching it for is worse than no log. Set explicitly rather than left to the framework's
        /// default, because it is the number that decides whether Follow mode is honest.
        /// </summary>
        public const int FlushSeconds = 1;

        /// <summary>
        /// Where the files go, relative to the install. Beside log4net's own <c>logs/</c> folder
        /// and inside the deploy target, which is the one directory the hardened systemd unit
        /// already grants write access to (<c>ReadWritePaths=/opt/tnpbx</c>) — nothing under
        /// <c>/var/log</c>, which that unit cannot write to and which is not ours.
        /// </summary>
        public const string RelativeDirectory = "logs/requests";

        /// <summary>
        /// How many files are kept. The logger starts a new one each day and whenever the current
        /// one passes <see cref="FileSizeLimitBytes"/>, so this is the ceiling on the whole folder:
        /// ten files of ten megabytes, and a redeploy clears them anyway (app-deploy.sh keeps only
        /// appsettings.json and Data/).
        /// </summary>
        public const int RetainedFileCount = 10;

        /// <summary>The absolute directory, given where the app is installed.</summary>
        public static string DirectoryIn(string contentRootPath) =>
            Path.GetFullPath(Path.Combine(contentRootPath, RelativeDirectory));

        /// <summary>
        /// Whether <see cref="SettingsKeys.WebRequestLog"/> leaves the log on. Read once at startup
        /// and not again: the logger is either built into the request pipeline or left out of it
        /// entirely, so changing this needs a restart of the service (D116).
        /// </summary>
        public static bool IsEnabled(IReadOnlyDictionary<string, string> settings) =>
            Toggles.Is(settings, SettingsKeys.WebRequestLog, EnabledByDefault);

        /// <summary>
        /// The file being written right now — the most recently touched one with our prefix — or
        /// null when there is none, which is what "the log is switched off" looks like from the
        /// Logs page. Never throws: a directory that is missing, or that this process cannot read,
        /// is the same answer as an empty one.
        ///
        /// Only the newest file, not all of them. It is the same bargain the app's own log makes:
        /// the page tails what is being written, and anything older is on the box for whoever wants
        /// to go and look.
        /// </summary>
        public static string? Newest(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return null;

                return new DirectoryInfo(directory)
                    .GetFiles(FileNamePrefix + "*")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Select(file => file.FullName)
                    .FirstOrDefault();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
