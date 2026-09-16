using System.Text;
using log4net;

namespace Techie.Pbx.Asterisk.Config
{
    public static class ConfFileWriter
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ConfFileWriter));

        /// <summary>
        /// Owner and group read/write, nothing for anyone else. The file holds SIP secrets, so the
        /// world must not read it; the asterisk process reads it through its group (D18).
        /// </summary>
        private const UnixFileMode ConfFileMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

        /// <summary>
        /// Writes a config file atomically: temp file in the same directory, fsync, rename over
        /// the target. Asterisk never sees a half-written file. Returns false if the content
        /// was already identical (nothing written, no reload needed).
        /// </summary>
        public static bool WriteAtomic(string directory, string fileName, string content)
        {
            var target = Path.Combine(directory, fileName);
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);

            if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
                return false;

            var temp = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temp, target, overwrite: true);

                // Explicitly, after the move: the mode the umask happened to give the temp file
                // is not good enough, because asterisk reads this file through its group.
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(target, ConfFileMode);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }

            Log.Info($"Wrote {target}");
            return true;
        }
    }
}
