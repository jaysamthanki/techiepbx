using System.Globalization;
using System.Text.RegularExpressions;
using log4net;

namespace Techie.Pbx.Backup
{
    /// <summary>
    /// The default backup folder (<c>/opt/tnpbx/backups</c>): how a backup there is named, and the
    /// retention that keeps the newest <see cref="Keep"/> of them (D171).
    ///
    /// Retention only ever deletes files with exactly the name this class gives a backup, and only
    /// runs after a backup to this folder: a file somebody put here by hand, or a <c>-o</c>
    /// backup anywhere, is never touched.
    /// </summary>
    public static partial class BackupDirectory
    {
        /// <summary>How many default-location backups are kept. A constant: D171 has no v1 settings for this.</summary>
        public const int Keep = 7;

        /// <summary>Root only: every backup holds every SIP secret on the box.</summary>
        private const UnixFileMode PrivateDirectoryMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        private static readonly ILog Log = LogManager.GetLogger(typeof(BackupDirectory));

        /// <summary><c>tnpbx-backup-YYYYMMDD-HHMMSS.tar.gz</c>, in UTC so the names sort in the order they were taken.</summary>
        public static string FileName(DateTime createdUtc) =>
            "tnpbx-backup-" + createdUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".tar.gz";

        /// <summary>
        /// Creates the folder if it is missing and makes it root's alone. A symlink is refused: the
        /// install folder above it belongs to the web user, and root following a link that user
        /// planted would write and prune wherever the link points.
        /// </summary>
        public static void Prepare(string path)
        {
            var directory = new DirectoryInfo(path);

            if (directory.LinkTarget != null)
                throw new BackupException($"{path} is a symbolic link; refusing to write backups through it.");

            Directory.CreateDirectory(path);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, PrivateDirectoryMode);
        }

        /// <summary>
        /// Deletes the oldest backups in <paramref name="path"/> beyond the newest <paramref name="keep"/>.
        /// Returns what it deleted.
        /// </summary>
        public static List<string> Prune(string path, int keep = Keep)
        {
            var doomed = Directory.EnumerateFiles(path)
                .Where(file => NamePattern().IsMatch(Path.GetFileName(file)))
                .OrderByDescending(file => Path.GetFileName(file), StringComparer.Ordinal)
                .Skip(keep)
                .ToList();

            foreach (var file in doomed)
            {
                File.Delete(file);
                Log.Info($"Retention: deleted {file} (keeping the newest {keep})");
            }

            return doomed;
        }

        [GeneratedRegex(@"^tnpbx-backup-[0-9]{8}-[0-9]{6}\.tar\.gz\z")]
        private static partial Regex NamePattern();
    }
}
