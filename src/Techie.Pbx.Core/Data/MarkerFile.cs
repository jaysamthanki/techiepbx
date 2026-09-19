using log4net;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// A one-bit answer kept as a file in the data folder: it exists or it does not (D26). A file
    /// rather than a row so that the answer survives anything that happens to the database, and so
    /// that an admin can see the state of a box with <c>ls</c>.
    ///
    /// Raising and clearing never throw: the marker is a reminder about work, and losing the
    /// reminder must not fail the work itself.
    /// </summary>
    public abstract class MarkerFile
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(MarkerFile));

        public string FilePath { get; }

        /// <summary>Whether the marker is up.</summary>
        public bool IsPending => File.Exists(this.FilePath);

        protected MarkerFile(string dataDirectory, string fileName)
        {
            this.FilePath = Path.Combine(dataDirectory, fileName);
        }

        public void Clear()
        {
            try
            {
                File.Delete(this.FilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Worst case the UI keeps saying there is something to do, which is the safe direction.
                Log.Warn($"Could not clear {this.FilePath}: {ex.Message}");
            }
        }

        public void Raise()
        {
            try
            {
                File.WriteAllText(this.FilePath, DateTimeOffset.UtcNow.ToString("u") + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not write {this.FilePath}: {ex.Message}");
            }
        }

        /// <summary>The data folder is the one the database is in, so a marker sits beside it.</summary>
        protected static string DirectoryOf(Database database) =>
            Path.GetDirectoryName(Path.GetFullPath(database.FilePath))!;
    }
}
