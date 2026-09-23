using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// Call flow control rows (F9). Only the switch's shape is stored here — name, code, the two
    /// destinations. Whether it is on lives in Asterisk's database, so nothing here reads or writes
    /// it, and a save raises the usual apply marker because the dialplan changes with the shape,
    /// never with the state.
    /// </summary>
    public class CallFlowControlRepository
    {
        private const string Columns =
            "CallFlowControlID, Name, FeatureCode, NormalDestinationType, NormalDestinationValue, " +
            "OverrideDestinationType, OverrideDestinationValue";

        private const int SqliteConstraintError = 19;

        /// <summary>
        /// How far a chain of one switch handing to another is followed before it is called a loop.
        /// The same bound the ring groups use (D54).
        /// </summary>
        private const int MaxChain = 10;

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public CallFlowControlRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        public void Delete(long callFlowControlID)
        {
            using var connection = this.database.Open();
            connection.Execute("DELETE FROM CallFlowControls WHERE CallFlowControlID = @callFlowControlID", new { callFlowControlID });

            this.pending.Raise();
        }

        public List<CallFlowControl> GetAll()
        {
            using var connection = this.database.Open();
            return connection.Query<CallFlowControl>($"SELECT {Columns} FROM CallFlowControls ORDER BY Name").ToList();
        }

        public CallFlowControl? GetByID(long callFlowControlID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<CallFlowControl>(
                $"SELECT {Columns} FROM CallFlowControls WHERE CallFlowControlID = @callFlowControlID", new { callFlowControlID });
        }

        public long Insert(CallFlowControl control)
        {
            this.ThrowIfInvalid(control);

            using var connection = this.database.Open();
            try
            {
                control.CallFlowControlID = connection.ExecuteScalar<long>(
                    "INSERT INTO CallFlowControls (Name, FeatureCode, NormalDestinationType, NormalDestinationValue, " +
                    "OverrideDestinationType, OverrideDestinationValue) " +
                    "VALUES (@Name, @FeatureCode, @NormalDestinationType, @NormalDestinationValue, " +
                    "@OverrideDestinationType, @OverrideDestinationValue); " +
                    "SELECT last_insert_rowid();",
                    control);

                this.pending.Raise();
                return control.CallFlowControlID;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A call flow control called '{control.Name}' or on {control.FeatureCode} already exists.");
            }
        }

        /// <summary>
        /// Saves the switch. A changed code takes every reference to the old one with it, in the
        /// same transaction (piece 37): destinations naming the switch, and phone keys on it.
        /// </summary>
        public void Update(CallFlowControl control)
        {
            var stored = this.GetByID(control.CallFlowControlID);

            this.ThrowIfInvalid(control);

            using var connection = this.database.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                if (stored != null && !string.Equals(stored.FeatureCode, control.FeatureCode, StringComparison.Ordinal))
                    Renumbering.CallFlowControl(connection, transaction, stored.FeatureCode, control.FeatureCode);

                var rows = connection.Execute(
                    "UPDATE CallFlowControls SET Name = @Name, FeatureCode = @FeatureCode, " +
                    "NormalDestinationType = @NormalDestinationType, NormalDestinationValue = @NormalDestinationValue, " +
                    "OverrideDestinationType = @OverrideDestinationType, OverrideDestinationValue = @OverrideDestinationValue " +
                    "WHERE CallFlowControlID = @CallFlowControlID",
                    control, transaction);
                if (rows == 0)
                    throw new ValidationFailedException($"CallFlowControlID {control.CallFlowControlID} does not exist.");

                transaction.Commit();
                this.pending.Raise();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A call flow control called '{control.Name}' or on {control.FeatureCode} already exists.");
            }
        }

        /// <summary>
        /// Follows every switch-to-switch hand-over from this one, down both of each switch's
        /// destinations, and answers whether any path comes back round. Both, because which one a
        /// call takes is decided by a phone at call time: a cycle that only closes while one switch
        /// is on is still a call that never ends the day somebody flips it. Only switches are
        /// followed — a loop through another feature is the admin's to avoid (D136).
        /// </summary>
        private static bool LoopsBack(CallFlowControl control, List<CallFlowControl> all)
        {
            var pending = new List<(CallFlowControl Control, int Depth)> { (control, 0) };
            var visited = new HashSet<string>(StringComparer.Ordinal);

            while (pending.Count > 0)
            {
                var (current, depth) = pending[^1];
                pending.RemoveAt(pending.Count - 1);

                if (depth >= MaxChain)
                    return true;

                foreach (var next in new[] { current.ToNormalDestination(), current.ToOverrideDestination() })
                {
                    if (next.Type != DestinationType.CallFlowControl)
                        continue;

                    if (string.Equals(next.Value, control.FeatureCode, StringComparison.Ordinal))
                        return true;

                    if (!visited.Add(next.Value))
                        continue;

                    var target = all.FirstOrDefault(c => string.Equals(c.FeatureCode, next.Value, StringComparison.Ordinal));
                    if (target != null)
                        pending.Add((target, depth + 1));
                }
            }

            return false;
        }

        /// <summary>
        /// The model's own rules, plus the ones that need the database: the code must not be one
        /// the system already dials on or another switch's, and both destinations have to still be
        /// somewhere a call can be sent — against the whole catalog, this switch included (D35,
        /// D136) — without two switches handing a call round in a circle.
        /// </summary>
        private void ThrowIfInvalid(CallFlowControl control)
        {
            var errors = control.Validate();
            var all = this.GetAll();

            if (CallFlowControl.IsValidFeatureCode(control.FeatureCode))
            {
                var parkCode = new SettingsRepository(this.database).Get(SettingsKeys.ParkingDtmfCode) ?? "";
                var clash = SystemCodes.Clash(control.FeatureCode, parkCode.Trim());

                if (clash != null)
                    errors.Add(clash);

                var taken = all.FirstOrDefault(c =>
                    c.CallFlowControlID != control.CallFlowControlID &&
                    string.Equals(c.FeatureCode, control.FeatureCode, StringComparison.Ordinal));

                if (taken != null)
                    errors.Add($"Call flow control '{taken.Name}' already uses {control.FeatureCode}.");
            }

            var named = all.FirstOrDefault(c =>
                c.CallFlowControlID != control.CallFlowControlID &&
                string.Equals(c.Name, control.Name, StringComparison.Ordinal));

            if (named != null)
                errors.Add($"A call flow control called '{control.Name}' already exists.");

            if (errors.Count == 0)
            {
                // The switch being saved is part of the world its destinations can point at, as its
                // new self: one switch handing to another is how they chain. Pointing at itself is
                // refused by the model, not by this list.
                var others = all.Where(c => c.CallFlowControlID != control.CallFlowControlID).Append(control).ToList();
                var extensions = new ExtensionRepository(this.database).GetAll();
                var ringGroups = new RingGroupRepository(this.database).GetAll();
                var announcements = new AnnouncementRepository(this.database).GetAll();
                var ivrs = new IvrRepository(this.database).GetAll();
                var conditions = new TimeConditionRepository(this.database).GetAll();

                if (DestinationCatalog.Find(extensions, ringGroups, announcements, ivrs, conditions, others, control.ToNormalDestination()) == null)
                    errors.Add("Where a call normally goes is not there any more. Choose another.");

                if (DestinationCatalog.Find(extensions, ringGroups, announcements, ivrs, conditions, others, control.ToOverrideDestination()) == null)
                    errors.Add("Where a call goes while switched on is not there any more. Choose another.");

                if (errors.Count == 0 && LoopsBack(control, others))
                    errors.Add("Those destinations hand the call from switch to switch and back to this one, so a call could go round for ever.");
            }

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
