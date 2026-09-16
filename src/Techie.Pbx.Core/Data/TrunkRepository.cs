using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    public class TrunkRepository
    {
        private const string Columns =
            "TrunkID, Name, ServerHost, ServerPort, Username, AuthUsername, Password, Register, " +
            "CallerIDName, CallerIDNumber, Codecs, MatchAddresses, Enabled";

        private const int SqliteConstraintError = 19;

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public TrunkRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        public void Delete(long trunkID)
        {
            using var connection = this.database.Open();

            try
            {
                connection.Execute("DELETE FROM Trunks WHERE TrunkID = @trunkID", new { trunkID });
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                // An outbound route still points at it. Deleting the route as well would be a
                // surprise; saying so is not.
                throw new ValidationFailedException(
                    "This trunk is still used by an outbound route. Delete the route first.");
            }

            this.pending.Raise();
        }

        public List<Trunk> GetAll()
        {
            using var connection = this.database.Open();
            return connection.Query<Trunk>($"SELECT {Columns} FROM Trunks ORDER BY Name").ToList();
        }

        public Trunk? GetByID(long trunkID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<Trunk>($"SELECT {Columns} FROM Trunks WHERE TrunkID = @trunkID", new { trunkID });
        }

        public Trunk? GetByName(string name)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<Trunk>($"SELECT {Columns} FROM Trunks WHERE Name = @name", new { name });
        }

        public long Insert(Trunk trunk)
        {
            ThrowIfInvalid(trunk);

            using var connection = this.database.Open();
            try
            {
                trunk.TrunkID = connection.ExecuteScalar<long>(
                    "INSERT INTO Trunks " +
                    "(Name, ServerHost, ServerPort, Username, AuthUsername, Password, Register, CallerIDName, CallerIDNumber, Codecs, MatchAddresses, Enabled) " +
                    "VALUES (@Name, @ServerHost, @ServerPort, @Username, @AuthUsername, @Password, @Register, @CallerIDName, @CallerIDNumber, @Codecs, @MatchAddresses, @Enabled); " +
                    "SELECT last_insert_rowid();",
                    trunk);

                this.pending.Raise();
                return trunk.TrunkID;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A trunk called {trunk.Name} already exists.");
            }
        }

        public void Update(Trunk trunk)
        {
            ThrowIfInvalid(trunk);

            using var connection = this.database.Open();
            try
            {
                var rows = connection.Execute(
                    "UPDATE Trunks SET " +
                    "Name = @Name, ServerHost = @ServerHost, ServerPort = @ServerPort, Username = @Username, " +
                    "AuthUsername = @AuthUsername, Password = @Password, Register = @Register, " +
                    "CallerIDName = @CallerIDName, CallerIDNumber = @CallerIDNumber, Codecs = @Codecs, " +
                    "MatchAddresses = @MatchAddresses, Enabled = @Enabled " +
                    "WHERE TrunkID = @TrunkID",
                    trunk);
                if (rows == 0)
                    throw new ValidationFailedException($"TrunkID {trunk.TrunkID} does not exist.");

                this.pending.Raise();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A trunk called {trunk.Name} already exists.");
            }
        }

        private static void ThrowIfInvalid(Trunk trunk)
        {
            var errors = trunk.Validate();
            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
