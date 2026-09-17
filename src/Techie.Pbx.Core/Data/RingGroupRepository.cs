using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    public class RingGroupRepository
    {
        private const string Columns =
            "RingGroupID, Number, Name, Strategy, Members, RingSeconds, CallerIDPrefix, " +
            "DestinationType, DestinationValue, Enabled";

        private const int SqliteConstraintError = 19;

        /// <summary>
        /// How far a chain of "nobody answered, try there" is followed before it is called a loop.
        /// Longer than anyone would build on purpose, short enough to stop quickly.
        /// </summary>
        private const int MaxChain = 10;

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public RingGroupRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        public void Delete(long ringGroupID)
        {
            using var connection = this.database.Open();
            connection.Execute("DELETE FROM RingGroups WHERE RingGroupID = @ringGroupID", new { ringGroupID });

            this.pending.Raise();
        }

        public List<RingGroup> GetAll()
        {
            using var connection = this.database.Open();
            return connection.Query<RingGroup>($"SELECT {Columns} FROM RingGroups ORDER BY CAST(Number AS INTEGER)").ToList();
        }

        public RingGroup? GetByID(long ringGroupID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<RingGroup>(
                $"SELECT {Columns} FROM RingGroups WHERE RingGroupID = @ringGroupID", new { ringGroupID });
        }

        public RingGroup? GetByNumber(string number)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<RingGroup>(
                $"SELECT {Columns} FROM RingGroups WHERE Number = @number", new { number });
        }

        public long Insert(RingGroup group)
        {
            this.ThrowIfInvalid(group);

            using var connection = this.database.Open();
            try
            {
                group.RingGroupID = connection.ExecuteScalar<long>(
                    "INSERT INTO RingGroups (Number, Name, Strategy, Members, RingSeconds, CallerIDPrefix, DestinationType, DestinationValue, Enabled) " +
                    "VALUES (@Number, @Name, @Strategy, @Members, @RingSeconds, @CallerIDPrefix, @DestinationType, @DestinationValue, @Enabled); " +
                    "SELECT last_insert_rowid();",
                    group);

                this.pending.Raise();
                return group.RingGroupID;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A ring group on {group.Number} already exists.");
            }
        }

        public void Update(RingGroup group)
        {
            this.ThrowIfInvalid(group);

            using var connection = this.database.Open();
            try
            {
                var rows = connection.Execute(
                    "UPDATE RingGroups SET Number = @Number, Name = @Name, Strategy = @Strategy, Members = @Members, " +
                    "RingSeconds = @RingSeconds, CallerIDPrefix = @CallerIDPrefix, DestinationType = @DestinationType, " +
                    "DestinationValue = @DestinationValue, Enabled = @Enabled WHERE RingGroupID = @RingGroupID",
                    group);
                if (rows == 0)
                    throw new ValidationFailedException($"RingGroupID {group.RingGroupID} does not exist.");

                this.pending.Raise();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A ring group on {group.Number} already exists.");
            }
        }

        /// <summary>
        /// Follows "nobody answered, try there" from this group and answers whether it comes back
        /// round to where it started. A dialplan loop is a call that never ends and a channel that
        /// never frees, so it is refused rather than written (D54).
        /// </summary>
        private static bool LoopsBack(RingGroup group, List<RingGroup> all)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { group.Number };
            var next = group.ToDestination();

            for (var hop = 0; hop < MaxChain; hop++)
            {
                if (next.Type != DestinationType.RingGroup)
                    return false;

                if (!seen.Add(next.Value))
                    return true;

                // The group being saved is the one in hand, not the older copy in the list.
                var target = string.Equals(next.Value, group.Number, StringComparison.Ordinal)
                    ? group
                    : all.FirstOrDefault(g => string.Equals(g.Number, next.Value, StringComparison.Ordinal));

                if (target == null)
                    return false;

                next = target.ToDestination();
            }

            return true;
        }

        /// <summary>
        /// The model's own rules, plus the ones that need the database: the number has to be free,
        /// every member has to be an extension that exists and is switched on, the destination has
        /// to still be something a call can be sent to (D35), and the group must not end up
        /// sending unanswered calls round in a circle.
        /// </summary>
        private void ThrowIfInvalid(RingGroup group)
        {
            var errors = group.Validate();
            var extensions = new ExtensionRepository(this.database).GetAll();
            var announcements = new AnnouncementRepository(this.database).GetAll();

            if (extensions.Any(e => string.Equals(e.Number, group.Number, StringComparison.Ordinal)))
                errors.Add($"Extension {group.Number} already uses that number.");

            // The check announcements make in the other direction (D57). One-sided would leave the
            // hole open from whichever side happened to be created second.
            var announcement = announcements.FirstOrDefault(a =>
                string.Equals(a.PlayExtension, group.Number, StringComparison.Ordinal));

            if (announcement != null)
                errors.Add($"Announcement '{announcement.Name}' already plays on {group.Number}.");

            // And the same both-ways check against an IVR's play extension (D59).
            var ivr = new IvrRepository(this.database).GetAll()
                .FirstOrDefault(i => string.Equals(i.PlayExtension, group.Number, StringComparison.Ordinal));

            if (ivr != null)
                errors.Add($"IVR '{ivr.Name}' already plays on {group.Number}.");

            // And against a time condition's, for the third time and the same reason (D63).
            var condition = new TimeConditionRepository(this.database).GetAll()
                .FirstOrDefault(t => string.Equals(t.PlayExtension, group.Number, StringComparison.Ordinal));

            if (condition != null)
                errors.Add($"Time condition '{condition.Name}' already uses {group.Number}.");

            foreach (var member in group.MemberList().Where(m => Extension.IsValidNumber(m)))
            {
                var extension = extensions.FirstOrDefault(e => string.Equals(e.Number, member, StringComparison.Ordinal));

                if (extension == null)
                    errors.Add($"There is no extension {member}.");
                else if (!extension.Enabled)
                    errors.Add($"Extension {member} is disabled, so it would never ring.");
            }

            if (errors.Count == 0)
            {
                var all = this.GetAll();

                var ivrs = new IvrRepository(this.database).GetAll();
                var conditions = new TimeConditionRepository(this.database).GetAll();

                if (DestinationCatalog.Find(extensions, all, announcements, ivrs, conditions, group.ToDestination()) == null)
                    errors.Add("That destination is not there any more. Choose another.");
                else if (LoopsBack(group, all))
                    errors.Add("That destination comes back round to this group, so a call nobody answers would ring for ever.");
            }

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
