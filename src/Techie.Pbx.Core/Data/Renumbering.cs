using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// What changing a number drags along with it (piece 37). Everything that points somewhere
    /// stores the number, not the row ID (D35), so an IVR, ring group, extension or call flow
    /// control that is renumbered would otherwise leave every reference to it pointing at a number
    /// nothing answers on.
    ///
    /// The one implementation the four repositories call from their Update, on the Update's own
    /// connection and transaction, so the renumber and every rewritten reference land together or
    /// not at all. Nothing is matched by substring: a destination is rewritten only when its stored
    /// pair is exactly the old <see cref="Destination"/>, and a list column only where a whole
    /// token is the old number.
    ///
    /// Deleting is deliberately not cascaded: a reference to something deleted already shows as
    /// gone in the picker and hangs up in the dialplan (D35).
    /// </summary>
    public static class Renumbering
    {
        /// <summary>Every stored destination, as table, type column and value column (D35).</summary>
        private static readonly (string Table, string TypeColumn, string ValueColumn)[] DestinationColumns =
        {
            ("CallFlowControls", "NormalDestinationType", "NormalDestinationValue"),
            ("CallFlowControls", "OverrideDestinationType", "OverrideDestinationValue"),
            ("InboundRoutes", "DestinationType", "DestinationValue"),
            ("IvrEntries", "DestinationType", "DestinationValue"),
            ("Ivrs", "DestinationType", "DestinationValue"),
            ("RingGroups", "DestinationType", "DestinationValue"),
            ("TimeConditionRules", "DestinationType", "DestinationValue"),
            ("TimeConditions", "ClosedDestinationType", "ClosedDestinationValue"),
            ("TimeConditions", "HolidayDestinationType", "HolidayDestinationValue"),
            ("TimeConditions", "OpenDestinationType", "OpenDestinationValue"),
        };

        /// <summary>
        /// A call flow control's code changed: every destination naming the switch, and every
        /// phone key on it, since a key stores the code too (F9).
        /// </summary>
        public static void CallFlowControl(SqliteConnection connection, SqliteTransaction transaction, string from, string to)
        {
            Destinations(connection, transaction,
                new Destination(DestinationType.CallFlowControl, from), new Destination(DestinationType.CallFlowControl, to));

            PhoneButtons(connection, transaction, new[] { PhoneButtonTarget.CallFlowControl }, from, to);
        }

        /// <summary>
        /// Rewrites every stored destination that is exactly <paramref name="from"/> to
        /// <paramref name="to"/>. Both have to be the same kind and both have to validate: a
        /// rewrite that produced a destination nothing could read would be worse than the dangling
        /// reference it replaced.
        /// </summary>
        public static void Destinations(SqliteConnection connection, SqliteTransaction transaction, Destination from, Destination to)
        {
            if (from.Type != to.Type)
                throw new InvalidOperationException($"Cannot renumber {from.Key} into a different kind, {to.Key}.");

            var errors = from.Validate().Concat(to.Validate()).ToList();
            if (errors.Count > 0)
                throw new InvalidOperationException($"Refusing to renumber {from.Key} to {to.Key}: {string.Join(" ", errors)}");

            // The table and column names are the constants above, never anything posted.
            foreach (var (table, typeColumn, valueColumn) in DestinationColumns)
            {
                connection.Execute(
                    $"UPDATE {table} SET {valueColumn} = @to WHERE {typeColumn} = @type AND {valueColumn} = @from",
                    new { type = from.Type.ToString(), from = from.Value, to = to.Value },
                    transaction);
            }
        }

        /// <summary>
        /// An extension's number changed. Two destination kinds are keyed by it — the phone and
        /// the mailbox — and three lists name it as well: other extensions' forwarding (D130), ring
        /// group members (D53), and phone keys, both the lamps that watch it and the line a phone
        /// registers as (D121, schema 020).
        /// </summary>
        public static void Extension(SqliteConnection connection, SqliteTransaction transaction, string from, string to)
        {
            foreach (var type in new[] { DestinationType.Extension, DestinationType.Voicemail })
                Destinations(connection, transaction, new Destination(type, from), new Destination(type, to));

            var forwarding = connection.Query<Extension>(
                "SELECT ExtensionID, Forwarding FROM Extensions WHERE Forwarding <> ''", transaction: transaction);

            foreach (var extension in forwarding)
            {
                var targets = extension.ForwardingList();
                if (!targets.Contains(from, StringComparer.Ordinal))
                    continue;

                connection.Execute(
                    "UPDATE Extensions SET Forwarding = @forwarding WHERE ExtensionID = @ExtensionID",
                    new { forwarding = string.Join(" ", Replace(targets, from, to)), extension.ExtensionID },
                    transaction);
            }

            var groups = connection.Query<RingGroup>(
                "SELECT RingGroupID, Members FROM RingGroups WHERE Members <> ''", transaction: transaction);

            foreach (var group in groups)
            {
                var members = group.MemberList();
                if (!members.Contains(from, StringComparer.Ordinal))
                    continue;

                connection.Execute(
                    "UPDATE RingGroups SET Members = @members WHERE RingGroupID = @RingGroupID",
                    new { members = string.Join(",", Replace(members, from, to)), group.RingGroupID },
                    transaction);
            }

            PhoneButtons(connection, transaction, new[] { PhoneButtonTarget.Blf, PhoneButtonTarget.Line }, from, to);
        }

        private static void PhoneButtons(SqliteConnection connection, SqliteTransaction transaction, string[] targetTypes, string from, string to)
        {
            connection.Execute(
                "UPDATE PhoneButtons SET TargetValue = @to WHERE TargetType IN @targetTypes AND TargetValue = @from",
                new { targetTypes, from, to },
                transaction);
        }

        private static IEnumerable<string> Replace(IEnumerable<string> tokens, string from, string to) =>
            tokens.Select(t => string.Equals(t, from, StringComparison.Ordinal) ? to : t);
    }
}
