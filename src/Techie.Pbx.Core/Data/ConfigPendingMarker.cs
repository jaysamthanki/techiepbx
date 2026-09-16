using log4net;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// "The database has changed since the last apply", kept as a file in the data folder next to
    /// the database (D26). Repository writes raise it; a successful apply clears it. A file rather
    /// than a row so that the answer survives anything that happens to the database, and so that
    /// an admin can see the state of a box with <c>ls</c>.
    /// </summary>
    public class ConfigPendingMarker
    {
        public const string FileName = "config-pending";

        private static readonly ILog Log = LogManager.GetLogger(typeof(ConfigPendingMarker));

        public string FilePath { get; }

        /// <summary>Whether config has been changed and not applied since.</summary>
        public bool IsPending => File.Exists(this.FilePath);

        /// <summary>The marker belongs beside the database, so the data folder is the one it is in.</summary>
        public ConfigPendingMarker(Database database)
            : this(Path.GetDirectoryName(Path.GetFullPath(database.FilePath))!)
        {
        }

        public ConfigPendingMarker(string dataDirectory)
        {
            this.FilePath = Path.Combine(dataDirectory, FileName);
        }

        public void Clear()
        {
            try
            {
                File.Delete(this.FilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Worst case the UI keeps saying an apply is due, which is the safe direction.
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
                // Never fail a save over the reminder that the save needs applying.
                Log.Warn($"Could not write {this.FilePath}: {ex.Message}");
            }
        }
    }
}
