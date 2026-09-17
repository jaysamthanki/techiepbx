using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    public class InboundRouteRepository
    {
        private const string Columns =
            "InboundRouteID, TrunkID, DID, CatchAll, DestinationType, DestinationValue, Description, Enabled";

        private const int SqliteConstraintError = 19;

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public InboundRouteRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        public void Delete(long inboundRouteID)
        {
            using var connection = this.database.Open();
            connection.Execute("DELETE FROM InboundRoutes WHERE InboundRouteID = @inboundRouteID", new { inboundRouteID });

            this.pending.Raise();
        }

        /// <summary>
        /// In the order the dialplan reads best: by trunk, DIDs in order, and each trunk's
        /// catch-all last.
        /// </summary>
        public List<InboundRoute> GetAll()
        {
            using var connection = this.database.Open();
            return connection.Query<InboundRoute>(
                $"SELECT {Columns} FROM InboundRoutes ORDER BY TrunkID, CatchAll, DID").ToList();
        }

        public InboundRoute? GetByID(long inboundRouteID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<InboundRoute>(
                $"SELECT {Columns} FROM InboundRoutes WHERE InboundRouteID = @inboundRouteID", new { inboundRouteID });
        }

        public long Insert(InboundRoute route)
        {
            this.ThrowIfInvalid(route);

            using var connection = this.database.Open();
            try
            {
                route.InboundRouteID = connection.ExecuteScalar<long>(
                    "INSERT INTO InboundRoutes (TrunkID, DID, CatchAll, DestinationType, DestinationValue, Description, Enabled) " +
                    "VALUES (@TrunkID, @DID, @CatchAll, @DestinationType, @DestinationValue, @Description, @Enabled); " +
                    "SELECT last_insert_rowid();",
                    route);

                this.pending.Raise();
                return route.InboundRouteID;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException(Taken(route));
            }
        }

        public void Update(InboundRoute route)
        {
            this.ThrowIfInvalid(route);

            using var connection = this.database.Open();
            try
            {
                var rows = connection.Execute(
                    "UPDATE InboundRoutes SET TrunkID = @TrunkID, DID = @DID, CatchAll = @CatchAll, " +
                    "DestinationType = @DestinationType, DestinationValue = @DestinationValue, " +
                    "Description = @Description, Enabled = @Enabled WHERE InboundRouteID = @InboundRouteID",
                    route);
                if (rows == 0)
                    throw new ValidationFailedException($"InboundRouteID {route.InboundRouteID} does not exist.");

                this.pending.Raise();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException(Taken(route));
            }
        }

        /// <summary>Which half of UNIQUE(TrunkID, DID) the admin has just run into.</summary>
        private static string Taken(InboundRoute route) =>
            route.CatchAll
                ? "That trunk already has a catch-all route. A trunk can only have one."
                : $"There is already a route for {route.DID} on that trunk.";

        /// <summary>
        /// The model's own rules, plus the two that need the database: the trunk has to exist and
        /// be enabled, and the destination has to still be something a call can be sent to (D35).
        /// </summary>
        private void ThrowIfInvalid(InboundRoute route)
        {
            var errors = route.Validate();

            if (route.TrunkID > 0)
            {
                var trunk = new TrunkRepository(this.database).GetByID(route.TrunkID);

                if (trunk == null)
                    errors.Add("That trunk no longer exists.");
                else if (!trunk.Enabled)
                    errors.Add($"The {trunk.Name} trunk is disabled, so no calls would arrive on it.");
            }

            if (errors.Count == 0)
            {
                // Every source the catalog knows about, so that a route can be pointed at a ring
                // group or an announcement as well as an extension or a mailbox (D35, D56).
                var extensions = new ExtensionRepository(this.database).GetAll();
                var ringGroups = new RingGroupRepository(this.database).GetAll();
                var announcements = new AnnouncementRepository(this.database).GetAll();

                if (DestinationCatalog.Find(extensions, ringGroups, announcements, route.ToDestination()) == null)
                    errors.Add("That destination is not there any more. Choose another.");
            }

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
