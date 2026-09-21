using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// The assignable keys on a phone (D121). Like <see cref="PhoneRepository"/> this raises no
    /// config-pending marker: a phone's config is generated per request and never written to
    /// /etc/asterisk (D79). The hints the lamps watch are not conditional on any of this — the
    /// dialplan carries one for every enabled extension and every parking slot whether a key
    /// points at it or not — so saving keys changes what the next provisioning fetch returns and
    /// nothing else.
    ///
    /// A save replaces the whole set inside one transaction, the way an IVR's digit map does
    /// (D59): the form posts all eight keys, and working out which one was changed, added or
    /// cleared would be three code paths where one will do.
    /// </summary>
    public class PhoneButtonRepository
    {
        private const string Columns = "PhoneButtonID, PhoneID, Position, TargetType, TargetValue";

        private const int SqliteConstraintError = 19;

        private readonly Database database;

        public PhoneButtonRepository(Database database)
        {
            this.database = database;
        }

        /// <summary>One phone's assigned keys, in key order. A key nobody assigned has no row.</summary>
        public List<PhoneButton> GetForPhone(long phoneID)
        {
            using var connection = this.database.Open();

            return connection.Query<PhoneButton>(
                $"SELECT {Columns} FROM PhoneButtons WHERE PhoneID = @phoneID ORDER BY Position", new { phoneID })
                .ToList();
        }

        /// <summary>
        /// Replaces every key on one phone. The buttons given are the assigned ones only; a key
        /// left blank on the form is absent from the list rather than a row saying "nothing".
        /// </summary>
        public void Replace(long phoneID, IEnumerable<PhoneButton> buttons)
        {
            var wanted = buttons.OrderBy(b => b.Position).ToList();
            this.ThrowIfInvalid(phoneID, wanted);

            using var connection = this.database.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                connection.Execute("DELETE FROM PhoneButtons WHERE PhoneID = @phoneID", new { phoneID }, transaction);

                foreach (var button in wanted)
                {
                    button.PhoneID = phoneID;
                    connection.Execute(
                        "INSERT INTO PhoneButtons (PhoneID, Position, TargetType, TargetValue) " +
                        "VALUES (@PhoneID, @Position, @TargetType, @TargetValue)",
                        button, transaction);
                }

                transaction.Commit();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException("That phone is not there any more, so its keys were not saved.");
            }
        }

        /// <summary>
        /// Each key's own rules, plus the ones that need the database: two keys cannot be the same
        /// key, and a key on an extension has to name an extension that exists. The foreign key
        /// would catch a phone that has gone, but a message an admin can act on beats a constraint
        /// violation.
        /// </summary>
        private void ThrowIfInvalid(long phoneID, List<PhoneButton> buttons)
        {
            var errors = new List<string>();
            var extensions = new ExtensionRepository(this.database).GetAll();

            foreach (var button in buttons)
            {
                foreach (var error in button.Validate())
                    errors.Add($"Key {button.Position}: {error}");

                if (string.Equals(button.TargetType, PhoneButtonTarget.Extension, StringComparison.Ordinal) &&
                    !extensions.Any(e => string.Equals(e.Number, button.TargetValue, StringComparison.Ordinal)))
                {
                    errors.Add($"Key {button.Position}: extension {button.TargetValue} is not there any more. Choose another.");
                }
            }

            if (buttons.Select(b => b.Position).Distinct().Count() != buttons.Count)
                errors.Add("Two keys cannot be in the same place.");

            if (phoneID <= 0)
                errors.Add("Keys can only be saved against a phone that exists.");

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
