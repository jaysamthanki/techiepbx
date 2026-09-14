using System.Text;
using log4net;

namespace Techie.Pbx.Asterisk.Config
{
    public static class ConfFileWriter
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ConfFileWriter));

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

                // Readable by the asterisk group, not the world (contains SIP secrets).
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);

                File.Move(temp, target, overwrite: true);
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
