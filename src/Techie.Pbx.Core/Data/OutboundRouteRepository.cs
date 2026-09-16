using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    public class OutboundRouteRepository
    {
        private const string Columns = "OutboundRouteID, Name, DialPattern, TrunkID, Priority, Enabled";
        private const int SqliteConstraintError = 19;

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public OutboundRouteRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        public void Delete(long outboundRouteID)
        {
            using var connection = this.database.Open();
            connection.Execute("DELETE FROM OutboundRoutes WHERE OutboundRouteID = @outboundRouteID", new { outboundRouteID });

            this.pending.Raise();
        }

        /// <summary>In the order they are tried: priority first, then name so ties are not random.</summary>
        public List<OutboundRoute> GetAll()
        {
            using var connection = this.database.Open();
            return connection.Query<OutboundRoute>($"SELECT {Columns} FROM OutboundRoutes ORDER BY Priority, Name").ToList();
        }

        public OutboundRoute? GetByID(long outboundRouteID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<OutboundRoute>(
                $"SELECT {Columns} FROM OutboundRoutes WHERE OutboundRouteID = @outboundRouteID", new { outboundRouteID });
        }

        public long Insert(OutboundRoute route)
        {
            this.ThrowIfInvalid(route);

            using var connection = this.database.Open();
            try
            {
                route.OutboundRouteID = connection.ExecuteScalar<long>(
                    "INSERT INTO OutboundRoutes (Name, DialPattern, TrunkID, Priority, Enabled) " +
                    "VALUES (@Name, @DialPattern, @TrunkID, @Priority, @Enabled); SELECT last_insert_rowid();",
                    route);

                this.pending.Raise();
                return route.OutboundRouteID;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A route called {route.Name} already exists.");
            }
        }

        public void Update(OutboundRoute route)
        {
            this.ThrowIfInvalid(route);

            using var connection = this.database.Open();
            try
            {
                var rows = connection.Execute(
                    "UPDATE OutboundRoutes SET Name = @Name, DialPattern = @DialPattern, TrunkID = @TrunkID, " +
                    "Priority = @Priority, Enabled = @Enabled WHERE OutboundRouteID = @OutboundRouteID",
                    route);
                if (rows == 0)
                    throw new ValidationFailedException($"OutboundRouteID {route.OutboundRouteID} does not exist.");

                this.pending.Raise();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A route called {route.Name} already exists.");
            }
        }

        /// <summary>
        /// The model's own rules, plus the one that needs the database: a route has to point at a
        /// trunk that exists and is switched on, or it is a route to nowhere.
        /// </summary>
        private void ThrowIfInvalid(OutboundRoute route)
        {
            var errors = route.Validate();

            if (route.TrunkID > 0)
            {
                var trunk = new TrunkRepository(this.database).GetByID(route.TrunkID);

                if (trunk == null)
                    errors.Add("That trunk no longer exists.");
                else if (!trunk.Enabled)
                    errors.Add($"The {trunk.Name} trunk is disabled, so no calls could go out over it.");
            }

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
