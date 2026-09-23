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

        private static void PhoneButtons(SqliteConnection connection, SqliteTransaction transaction, string[] targetTypes, string from, string to)
        {
            connection.Execute(
                "UPDATE PhoneButtons SET TargetValue = @to WHERE TargetType IN @targetTypes AND TargetValue = @from",
                new { targetTypes, from, to },
                transaction);
        }
    }
}
