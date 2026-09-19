using System.Globalization;
using Techie.Pbx.Core.Diagnostics;

namespace Techie.Pbx.Web.Pages.Status
{
    /// <summary>
    /// What the "_Lines" partial needs for one poll of the log viewer. The header line's pieces
    /// are worked out here rather than in the view, so the view is just the four cases the
    /// instructions for this page describe: no file configured, no file yet, unreadable, and here
    /// are the lines.
    /// </summary>
    public class LogLinesView
    {
        /// <summary>The file size as the header line shows it.</summary>
        public string FileSizeText => this.Result.FileSizeBytes >= 1024 * 1024
            ? $"{this.Result.FileSizeBytes / (1024.0 * 1024.0):0.0} MB"
            : $"{this.Result.FileSizeBytes / 1024.0:0.0} KB";

        /// <summary>The trimmed filter that was applied, or null when there was none.</summary>
        public string? Filter { get; set; }

        /// <summary>The last-written time as the header line shows it, or "—" when there is none.</summary>
        public string LastWriteText => this.Result.LastWriteUtc
            ?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) ?? "—";

        /// <summary>How many lines were asked for.</summary>
        public int Lines { get; set; }

        /// <summary>Where this source's file is today, or null when it has none — the app log with no file appender.</summary>
        public string? Path { get; set; }

        public LogTailResult Result { get; set; } = new();

        public LogSource Source { get; set; } = null!;
    }
}
