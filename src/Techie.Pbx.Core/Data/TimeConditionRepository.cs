using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// Time condition rows and their rules. The two are always read and written together — a
    /// condition is its open hours and its holidays — so a <see cref="TimeCondition"/> carries its
    /// rules and a save replaces them wholesale inside one transaction, the way an IVR's digit map
    /// is written (D59, D62).
    /// </summary>
    public class TimeConditionRepository
    {
        private const string Columns =
            "TimeConditionID, Name, Description, PlayExtension, OpenDestinationType, OpenDestinationValue, " +
            "ClosedDestinationType, ClosedDestinationValue, HolidayDestinationType, HolidayDestinationValue, Enabled";

        private const string RuleColumns =
            "TimeConditionRuleID, TimeConditionID, Kind, DaysMask, StartTime, EndTime, HolidayDate, " +
            "DestinationType, DestinationValue, SortOrder";

        private const int SqliteConstraintError = 19;

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public TimeConditionRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        public void Delete(long timeConditionID)
        {
            using var connection = this.database.Open();

            // The rules go with it: TimeConditionRules.TimeConditionID is ON DELETE CASCADE,
            // because a rule has no life of its own once its condition is gone.
            connection.Execute("DELETE FROM TimeConditions WHERE TimeConditionID = @timeConditionID", new { timeConditionID });

            this.pending.Raise();
        }

        public List<TimeCondition> GetAll()
        {
            using var connection = this.database.Open();

            var conditions = connection.Query<TimeCondition>($"SELECT {Columns} FROM TimeConditions ORDER BY Name").ToList();
            var rules = connection.Query<TimeConditionRule>($"SELECT {RuleColumns} FROM TimeConditionRules").ToList();

            foreach (var condition in conditions)
                condition.Rules = InRuleOrder(rules.Where(r => r.TimeConditionID == condition.TimeConditionID));

            return conditions;
        }

        public TimeCondition? GetByID(long timeConditionID)
        {
            using var connection = this.database.Open();

            var condition = connection.QuerySingleOrDefault<TimeCondition>(
                $"SELECT {Columns} FROM TimeConditions WHERE TimeConditionID = @timeConditionID", new { timeConditionID });
            if (condition == null)
                return null;

            condition.Rules = InRuleOrder(connection.Query<TimeConditionRule>(
                $"SELECT {RuleColumns} FROM TimeConditionRules WHERE TimeConditionID = @timeConditionID", new { timeConditionID }));

            return condition;
        }

        public long Insert(TimeCondition condition)
        {
            this.ThrowIfInvalid(condition);

            using var connection = this.database.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                condition.TimeConditionID = connection.ExecuteScalar<long>(
                    "INSERT INTO TimeConditions (Name, Description, PlayExtension, OpenDestinationType, OpenDestinationValue, " +
                    "ClosedDestinationType, ClosedDestinationValue, HolidayDestinationType, HolidayDestinationValue, Enabled) " +
                    "VALUES (@Name, @Description, @PlayExtension, @OpenDestinationType, @OpenDestinationValue, " +
                    "@ClosedDestinationType, @ClosedDestinationValue, @HolidayDestinationType, @HolidayDestinationValue, @Enabled); " +
                    "SELECT last_insert_rowid();",
                    condition, transaction);

                WriteRules(connection, transaction, condition);
                transaction.Commit();

                this.pending.Raise();
                return condition.TimeConditionID;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A time condition called '{condition.Name}' already exists.");
            }
        }

        /// <summary>
        /// Saves the condition. A changed play extension takes every reference to the old one with
        /// it, in the same transaction (piece 37) — the sweep runs before the row, so a failure on
        /// the row's own UNIQUE constraint rolls the references back too. Only a number that
        /// changes to another number is swept; clearing it is a delete as far as a reference is
        /// concerned, and deletes are not cascaded (D35).
        /// </summary>
        public void Update(TimeCondition condition)
        {
            this.ThrowIfInvalid(condition);

            var stored = this.GetByID(condition.TimeConditionID);
            var renumbered = stored != null && stored.PlayExtension.Length > 0 && condition.PlayExtension.Length > 0 &&
                !string.Equals(stored.PlayExtension, condition.PlayExtension, StringComparison.Ordinal);

            using var connection = this.database.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                if (renumbered)
                    Renumbering.Destinations(connection, transaction, stored!.ToDestination(), condition.ToDestination());

                var rows = connection.Execute(
                    "UPDATE TimeConditions SET Name = @Name, Description = @Description, PlayExtension = @PlayExtension, " +
                    "OpenDestinationType = @OpenDestinationType, OpenDestinationValue = @OpenDestinationValue, " +
                    "ClosedDestinationType = @ClosedDestinationType, ClosedDestinationValue = @ClosedDestinationValue, " +
                    "HolidayDestinationType = @HolidayDestinationType, HolidayDestinationValue = @HolidayDestinationValue, " +
                    "Enabled = @Enabled WHERE TimeConditionID = @TimeConditionID",
                    condition, transaction);
                if (rows == 0)
                    throw new ValidationFailedException($"TimeConditionID {condition.TimeConditionID} does not exist.");

                // The rules are replaced rather than merged, for the reason an IVR's digit map is
                // (D59): the form posts the whole condition, and working out which rule was edited,
                // added or removed would be three code paths where one will do.
                connection.Execute("DELETE FROM TimeConditionRules WHERE TimeConditionID = @TimeConditionID", condition, transaction);
                WriteRules(connection, transaction, condition);
                transaction.Commit();

                this.pending.Raise();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A time condition called '{condition.Name}' already exists.");
            }
        }

        /// <summary>
        /// One destination still being somewhere a call can go. Said once here rather than four
        /// times below, because a condition has four of them and they all fail the same way.
        /// </summary>
        private static void CheckDestination(
            List<string> errors,
            List<Extension> extensions,
            List<RingGroup> ringGroups,
            List<Announcement> announcements,
            List<Ivr> ivrs,
            List<TimeCondition> timeConditions,
            List<CallFlowControl> callFlowControls,
            Destination destination,
            string what)
        {
            if (DestinationCatalog.Find(extensions, ringGroups, announcements, ivrs, timeConditions, callFlowControls, destination) == null)
                errors.Add($"Where a call in {what} goes is not there any more. Choose another.");
        }

        /// <summary>
        /// The rules in the order the dialplan will write them: open hours first, then the holidays
        /// by date. Both halves are sorted again by the model when it renders, so this is only so
        /// that a form shows them the way the conf file will.
        /// </summary>
        private static List<TimeConditionRule> InRuleOrder(IEnumerable<TimeConditionRule> rules) =>
            rules
                .OrderBy(r => r.Kind)
                .ThenBy(r => r.SortOrder)
                .ThenBy(r => r.HolidayDate, StringComparer.Ordinal)
                .ThenBy(r => r.StartTime, StringComparer.Ordinal)
                .ToList();

        private static void WriteRules(SqliteConnection connection, SqliteTransaction transaction, TimeCondition condition)
        {
            foreach (var rule in condition.Rules)
            {
                rule.TimeConditionID = condition.TimeConditionID;
                connection.Execute(
                    "INSERT INTO TimeConditionRules (TimeConditionID, Kind, DaysMask, StartTime, EndTime, HolidayDate, " +
                    "DestinationType, DestinationValue, SortOrder) " +
                    "VALUES (@TimeConditionID, @Kind, @DaysMask, @StartTime, @EndTime, @HolidayDate, " +
                    "@DestinationType, @DestinationValue, @SortOrder)",
                    rule, transaction);
            }
        }

        /// <summary>
        /// The model's own rules — the name, the times, the dates, and the refusal to point at
        /// itself — plus the ones that need the database: the play extension has to be a number
        /// nothing else has claimed (D57), and every destination, the three cases and every holiday
        /// override, has to still be somewhere a call can be sent (D35).
        /// </summary>
        private void ThrowIfInvalid(TimeCondition condition)
        {
            var errors = condition.Validate();
            var extensions = new ExtensionRepository(this.database).GetAll();
            var announcements = new AnnouncementRepository(this.database).GetAll();
            var ringGroups = new RingGroupRepository(this.database).GetAll();
            var ivrs = new IvrRepository(this.database).GetAll();
            var all = this.GetAll();

            if (condition.PlayExtension.Length > 0)
            {
                var number = condition.PlayExtension;

                if (extensions.Any(e => string.Equals(e.Number, number, StringComparison.Ordinal)))
                    errors.Add($"Extension {number} already uses that number.");

                if (ringGroups.Any(g => string.Equals(g.Number, number, StringComparison.Ordinal)))
                    errors.Add($"Ring group {number} already uses that number.");

                var announcement = announcements.FirstOrDefault(a =>
                    string.Equals(a.PlayExtension, number, StringComparison.Ordinal));

                if (announcement != null)
                    errors.Add($"Announcement '{announcement.Name}' already plays on {number}.");

                var ivr = ivrs.FirstOrDefault(i => string.Equals(i.PlayExtension, number, StringComparison.Ordinal));

                if (ivr != null)
                    errors.Add($"IVR '{ivr.Name}' already plays on {number}.");

                var clash = all.FirstOrDefault(t =>
                    t.TimeConditionID != condition.TimeConditionID &&
                    string.Equals(t.PlayExtension, number, StringComparison.Ordinal));

                if (clash != null)
                    errors.Add($"Time condition '{clash.Name}' already uses {number}.");
            }

            if (errors.Count == 0)
            {
                // The condition being saved is part of the world its own destinations can point at,
                // because one condition handing over to another is how "closed" gets a second set
                // of hours. Pointing at itself is refused by the model, not by this list.
                var others = all.Where(t => t.TimeConditionID != condition.TimeConditionID).Append(condition).ToList();
                var controls = new CallFlowControlRepository(this.database).GetAll();

                CheckDestination(errors, extensions, ringGroups, announcements, ivrs, others, controls, condition.ToOpenDestination(), "open hours");
                CheckDestination(errors, extensions, ringGroups, announcements, ivrs, others, controls, condition.ToClosedDestination(), "closed hours");
                CheckDestination(errors, extensions, ringGroups, announcements, ivrs, others, controls, condition.ToHolidayDestination(), "holidays");

                foreach (var rule in condition.HolidayRules().Where(r => r.HasOverride))
                {
                    CheckDestination(errors, extensions, ringGroups, announcements, ivrs, others, controls,
                        rule.ToDestination()!, $"the holiday on {rule.HolidayDate}");
                }
            }

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
