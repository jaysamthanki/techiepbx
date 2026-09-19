namespace Techie.Pbx.Core.Diagnostics
{
    /// <summary>
    /// What <see cref="LogTail.Read"/> found. <see cref="Exists"/> false means there is no file at
    /// that path yet, which the Logs page shows differently from <see cref="Error"/>, which means
    /// the file is there but could not be opened — a permission problem on the lab VM being the
    /// obvious one, since the web process is not root and only ever a group member (D3, D18).
    /// </summary>
    public class LogTailResult
    {
        /// <summary>A short sentence, never an exception, for a file that exists but could not be read.</summary>
        public string? Error { get; set; }

        /// <summary>Whether there is a file at the path at all.</summary>
        public bool Exists { get; set; }

        public long FileSizeBytes { get; set; }

        public DateTimeOffset? LastWriteUtc { get; set; }

        /// <summary>The matching lines, oldest first, already capped to the requested count.</summary>
        public List<string> Lines { get; set; } = new();

        /// <summary>
        /// True when the file was longer than the scan cap, so everything before the scanned
        /// window was never looked at — a filter that finds nothing here might still match
        /// somewhere older in the file.
        /// </summary>
        public bool ScanCapped { get; set; }
    }
}
