using Dapper;
using log4net;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// The Settings key/value table: AMI connection details (including the secret), the conf
    /// directory and the SIP transport settings. Only keys listed in <see cref="SettingsKeys"/>
    /// can be written, and only values <see cref="SettingsValidation"/> accepts.
    /// </summary>
    public class SettingsRepository
    {
        private const string Columns = "SettingID, \"Key\", Value";

        private static readonly ILog Log = LogManager.GetLogger(typeof(SettingsRepository));

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public SettingsRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        /// <summary>
        /// Removes a setting, which puts it back to its built-in default. Every one of these keys
        /// is read while config is generated, so this is a config change like any other (D69).
        /// </summary>
        public void Delete(string key)
        {
            using var connection = this.database.Open();
            connection.Execute("DELETE FROM Settings WHERE \"Key\" = @key", new { key });

            this.pending.Raise();
            Log.Info($"Setting {key} cleared");
        }

        public string? Get(string key)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<string>("SELECT Value FROM Settings WHERE \"Key\" = @key", new { key });
        }

        /// <summary>
        /// Every setting, as one map. Callers that need several values read once and pass the
        /// map around rather than querying per key.
        /// </summary>
        public Dictionary<string, string> GetAll()
        {
            using var connection = this.database.Open();
            var rows = connection.Query<Setting>($"SELECT {Columns} FROM Settings ORDER BY \"Key\"");

            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in rows)
                settings[row.Key] = row.Value;

            return settings;
        }

        /// <summary>
        /// Inserts or replaces one setting. Throws <see cref="ValidationFailedException"/> for an
        /// unknown key — a typo should fail loudly, not quietly store a value nothing ever reads —
        /// and for a value the key cannot hold, such as a port that is not a number (D67).
        /// </summary>
        public void Set(string key, string value)
        {
            ThrowIfInvalid(key, value);

            using var connection = this.database.Open();
            connection.Execute(
                "INSERT INTO Settings (\"Key\", Value) VALUES (@key, @value) " +
                "ON CONFLICT(\"Key\") DO UPDATE SET Value = excluded.Value",
                new { key, value });

            this.pending.Raise();

            // Never log a secret's value, and there is no reason to hide the others.
            Log.Info(SettingsKeys.IsSecret(key) ? $"Setting {key} updated" : $"Setting {key} = {value}");
        }

        private static void ThrowIfInvalid(string key, string value)
        {
            var errors = SettingsValidation.Errors(key, value);

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
