using log4net;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web
{
    /// <summary>
    /// The one <see cref="Database"/> the web app uses, opened once at startup. A static holder
    /// rather than a registered service, because pages and controllers construct their own
    /// repositories over it with <c>new</c> (D8). A Database is only a connection string, so
    /// constructing repositories per request costs nothing.
    /// </summary>
    public static class PbxDatabase
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(PbxDatabase));

        private static Database? database;

        public static Database Current =>
            database ?? throw new InvalidOperationException("The database is not open yet; PbxDatabase.Open runs at startup.");

        /// <summary>
        /// Creates the database file if it is missing and applies any pending schema scripts.
        /// The directory has to be writable by the web user: the installer creates it.
        /// </summary>
        public static void Open(string filePath)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var opened = new Database(filePath);
            opened.Migrate();
            database = opened;

            Log.Info($"Database ready at {opened.FilePath}");
        }
    }
}
