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
        private static readonly ILog Log = LogManager.GetLogger(typeof(Database));

        private readonly string _connectionString;

        public string FilePath { get; }

        public Database(string filePath)
        {
            FilePath = filePath;
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = filePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true,
            }.ToString();
        }

        public SqliteConnection Open()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// Creates the database if needed and applies any pending schema scripts.
        /// </summary>
        public void Migrate()
        {
            using var connection = Open();

            // Secrets live in here; keep it private to the service account.
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

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
