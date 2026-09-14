using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    public class ExtensionRepository
    {
        private const string Columns = "ExtensionID, Number, Name, Secret, Enabled";
        private const int SqliteConstraintError = 19;

        private readonly Database _database;

        public ExtensionRepository(Database database)
        {
            _database = database;
        }

        public List<Extension> GetAll()
        {
            using var connection = _database.Open();
            return connection.Query<Extension>($"SELECT {Columns} FROM Extensions ORDER BY CAST(Number AS INTEGER)").ToList();
        }

        public Extension? GetByNumber(string number)
        {
            using var connection = _database.Open();
            return connection.QuerySingleOrDefault<Extension>($"SELECT {Columns} FROM Extensions WHERE Number = @number", new { number });
        }

        public long Insert(Extension extension)
        {
            ThrowIfInvalid(extension);

            using var connection = _database.Open();
            try
            {
                extension.ExtensionID = connection.ExecuteScalar<long>(
                    "INSERT INTO Extensions (Number, Name, Secret, Enabled) VALUES (@Number, @Name, @Secret, @Enabled); SELECT last_insert_rowid();",
                    extension);
                return extension.ExtensionID;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"Extension {extension.Number} already exists.");
            }
        }

        public void Update(Extension extension)
        {
            ThrowIfInvalid(extension);

            using var connection = _database.Open();
            try
            {
                var rows = connection.Execute(
                    "UPDATE Extensions SET Number = @Number, Name = @Name, Secret = @Secret, Enabled = @Enabled WHERE ExtensionID = @ExtensionID",
                    extension);
                if (rows == 0)
                    throw new ValidationFailedException($"ExtensionID {extension.ExtensionID} does not exist.");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"Extension {extension.Number} already exists.");
            }
        }

        public void Delete(long extensionID)
        {
            using var connection = _database.Open();
            connection.Execute("DELETE FROM Extensions WHERE ExtensionID = @extensionID", new { extensionID });
        }

        private static void ThrowIfInvalid(Extension extension)
        {
            var errors = extension.Validate();
            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
