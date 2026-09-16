using Dapper;
using log4net;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// The Settings key/value table: AMI connection details (including the secret), the conf
    /// directory and the SIP transport settings. Only keys listed in <see cref="SettingsKeys"/>
    /// can be written.
    /// </summary>
    public class SettingsRepository
    {
        private const string Columns = "SettingID, \"Key\", Value";
        private const int MaxValueLength = 1024;

        private static readonly ILog Log = LogManager.GetLogger(typeof(SettingsRepository));

        private readonly Database _database;

        public SettingsRepository(Database database)
        {
            _database = database;
        }

        /// <summary>
        /// Every setting, as one map. Callers that need several values read once and pass the
        /// map around rather than querying per key.
        /// </summary>
        public Dictionary<string, string> GetAll()
        {
            using var connection = _database.Open();
            var rows = connection.Query<Setting>($"SELECT {Columns} FROM Settings ORDER BY \"Key\"");

            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in rows)
                settings[row.Key] = row.Value;

            return settings;
        }

        public string? Get(string key)
        {
            using var connection = _database.Open();
            return connection.QuerySingleOrDefault<string>("SELECT Value FROM Settings WHERE \"Key\" = @key", new { key });
        }

        /// <summary>
        /// Inserts or replaces one setting. Throws <see cref="ValidationFailedException"/> for an
        /// unknown key: a typo should fail loudly, not quietly store a value nothing ever reads.
        /// </summary>
        public void Set(string key, string value)
        {
            ThrowIfInvalid(key, value);

            using var connection = _database.Open();
            connection.Execute(
                "INSERT INTO Settings (\"Key\", Value) VALUES (@key, @value) " +
                "ON CONFLICT(\"Key\") DO UPDATE SET Value = excluded.Value",
                new { key, value });

            // Never log a secret's value, and there is no reason to hide the others.
            Log.Info(SettingsKeys.IsSecret(key) ? $"Setting {key} updated" : $"Setting {key} = {value}");
        }

        /// <summary>
        /// Removes a setting, which puts it back to its built-in default.
        /// </summary>
        public void Delete(string key)
        {
            using var connection = _database.Open();
            connection.Execute("DELETE FROM Settings WHERE \"Key\" = @key", new { key });
            Log.Info($"Setting {key} cleared");
        }

        private static void ThrowIfInvalid(string key, string value)
        {
            var errors = new List<string>();

            if (!SettingsKeys.IsKnown(key))
                errors.Add($"'{key}' is not a known setting.");

            if (value == null)
                errors.Add("Setting value is required.");
            else if (value.Length > MaxValueLength)
                errors.Add($"Setting value must be {MaxValueLength} characters or fewer.");

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
