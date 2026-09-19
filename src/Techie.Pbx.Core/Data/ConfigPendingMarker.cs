namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// "The database has changed since the last apply", kept as a file in the data folder next to
    /// the database (D26). Repository writes raise it; a successful apply clears it. Settings are
    /// the exception: only an Asterisk-scoped one raises it, because nothing else reaches a conf
    /// file (D103).
    /// </summary>
    public class ConfigPendingMarker : MarkerFile
    {
        public const string FileName = "config-pending";

        /// <summary>The marker belongs beside the database, so the data folder is the one it is in.</summary>
        public ConfigPendingMarker(Database database)
            : this(DirectoryOf(database))
        {
        }

        public ConfigPendingMarker(string dataDirectory)
            : base(dataDirectory, FileName)
        {
        }
    }
}
