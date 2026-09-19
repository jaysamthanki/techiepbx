using System.Text;

namespace Techie.Pbx.Core.Diagnostics
{
    /// <summary>
    /// Reads the last part of a log file cheaply: seeks to <c>maxScanBytes</c> from the end
    /// instead of reading the whole thing, so a request against a hundreds-of-megabytes
    /// messages.log costs one seek and a bounded read rather than a full scan. A pure function of
    /// a path in and a result out — it does no logging of its own and never throws for a file that
    /// is missing or unreadable, because the Logs page (Techie.Pbx.Web.Pages.Status) has to show
    /// those the same way it shows any other line.
    /// </summary>
    public static class LogTail
    {
        /// <summary>Asterisk's own default, used when asterisk.conf leaves it unset.</summary>
        public const string DefaultLogDirectory = "/var/log/asterisk";

        /// <summary>
        /// The last <paramref name="maxLines"/> lines of <paramref name="path"/>, optionally kept
        /// to only the ones containing <paramref name="filter"/> (case-insensitive). Reads at most
        /// <paramref name="maxScanBytes"/> from the end of the file before filtering, so a filter
        /// that matches nothing in the scanned window is reported as such rather than as a reason
        /// to read further back.
        /// </summary>
        public static LogTailResult Read(string path, int maxLines, string? filter, int maxScanBytes)
        {
            if (!File.Exists(path))
                return new LogTailResult { Exists = false };

            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                var length = stream.Length;
                var start = Math.Max(0, length - maxScanBytes);
                var scanCapped = start > 0;

                stream.Seek(start, SeekOrigin.Begin);

                var buffer = new byte[length - start];
                var read = 0;
                while (read < buffer.Length)
                {
                    var got = stream.Read(buffer, read, buffer.Length - read);
                    if (got == 0)
                        break;

                    read += got;
                }

                var lines = SplitLines(Encoding.UTF8.GetString(buffer, 0, read));

                // The scan did not start at the beginning of the file, so whatever came out first
                // is a fragment of a line that was cut off, not a line the file actually has.
                if (start > 0 && lines.Count > 0)
                    lines.RemoveAt(0);

                if (!string.IsNullOrEmpty(filter))
                    lines = lines.Where(line => line.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                if (lines.Count > maxLines)
                    lines = lines.GetRange(lines.Count - maxLines, maxLines);

                return new LogTailResult
                {
                    Exists = true,
                    FileSizeBytes = length,
                    LastWriteUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero),
                    Lines = lines,
                    ScanCapped = scanCapped,
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Contents never go in the log (the instructions for this piece are explicit
                // about that), so the caller logs this Error sentence, not the exception.
                return new LogTailResult { Exists = true, Error = $"Could not read {path}: {ex.Message}" };
            }
        }

        /// <summary>
        /// Splits on '\n', trims a '\r' left over from a CRLF line ending, and drops the single
        /// empty entry a trailing newline otherwise leaves at the end — the file has one line per
        /// '\n', not an extra blank one after the last.
        /// </summary>
        private static List<string> SplitLines(string text)
        {
            var lines = text.Split('\n').ToList();

            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].EndsWith('\r'))
                    lines[i] = lines[i][..^1];
            }

            if (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);

            return lines;
        }
    }
}
