using System.Reflection;
using Dapper;
using log4net;
using Microsoft.Data.Sqlite;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// The SQLite database: source of truth for all PBX configuration.
    /// Schema changes are numbered scripts in Data/Schema, tracked with PRAGMA user_version.
    /// </summary>
    public class Database
    {
        /// <summary>
        /// Where the database goes when Database:Path says nothing: a Data folder inside the
        /// install, so that copying the app folder copies everything it owns (D25).
        /// </summary>
        public const string DefaultPath = "Data/tnpbx.db";

        /// <summary>The appsettings.json key the web app and the backup CLI both read the path from.</summary>
        public const string PathSetting = "Database:Path";

        private static readonly ILog Log = LogManager.GetLogger(typeof(Database));

        private readonly string connectionString;

        public string FilePath { get; }

        /// <summary>
        /// The schema version the scripts in this build migrate up to. A database newer than this
        /// was written by a newer build, and nothing here can migrate it down (D171).
        /// </summary>
        public static int LatestSchemaVersion => LoadSchemaScripts().Max(s => s.Version);

        public Database(string filePath)
        {
            this.FilePath = filePath;
            this.connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = filePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true,
            }.ToString();
        }

        /// <summary>
        /// Creates the database if needed and applies any pending schema scripts.
        /// </summary>
        public void Migrate()
        {
            using var connection = this.Open();

            // Secrets live in here; keep it private to the service account.
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(this.FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var current = connection.ExecuteScalar<long>("PRAGMA user_version");

            foreach (var (version, sql) in LoadSchemaScripts().Where(s => s.Version > current))
            {
                using var transaction = connection.BeginTransaction();
                connection.Execute(sql, transaction: transaction);
                connection.Execute($"PRAGMA user_version = {version}", transaction: transaction);
                transaction.Commit();

                Log.Info($"Applied schema version {version}");
            }
        }

        public SqliteConnection Open()
        {
            var connection = new SqliteConnection(this.connectionString);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// A path from configuration, or the default when it says nothing; a relative path is
        /// relative to the install rather than to whatever directory the process started in (D25).
        /// The one rule for every configured path, so the web app and the backup CLI cannot
        /// disagree about where a file is.
        /// </summary>
        public static string ResolvePath(string? configured, string fallback, string contentRoot)
        {
            var path = string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
            return Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path);
        }

        private static List<(int Version, string Sql)> LoadSchemaScripts()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var scripts = new List<(int, string)>();

            foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("schema/")))
            {
                // schema/001_initial.sql -> 1
                var fileName = resource["schema/".Length..];
                var version = int.Parse(fileName[..fileName.IndexOf('_')]);

                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                scripts.Add((version, reader.ReadToEnd()));
            }

            return scripts.OrderBy(s => s.Item1).ToList();
        }
    }
}
