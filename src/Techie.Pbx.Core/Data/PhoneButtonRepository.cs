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
    ///
    /// Since schema 020 this table also holds which extension a phone registers as — the line key
    /// — so a save here is what assigns a phone to somebody, and the rules about that live in
    /// <see cref="ThrowIfInvalid"/>.
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
        /// Every line key on every phone: which extension each phone registers as, since schema 020
        /// moved that off the phone's own row. The phones table and the status page read it to say
        /// what a phone is, and a save reads it to refuse registering two phones as one extension.
        /// </summary>
        public List<PhoneButton> GetLines()
        {
            using var connection = this.database.Open();

            return connection.Query<PhoneButton>(
                $"SELECT {Columns} FROM PhoneButtons WHERE TargetType = @line ORDER BY PhoneID, Position",
                new { line = PhoneButtonTarget.Line })
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
        /// The set's own rules (<see cref="PhoneButton.ValidateSet"/> — every key valid, no two in
        /// one place, the lines leading from key 1, and any other key free to be left blank
        /// wherever the admin wants the gap), plus the two that need the database:
        ///
        /// <list type="bullet">
        /// <item>A key on an extension has to name an extension that exists. The key stores the
        /// number rather than the ID, so nothing but this check stands between an admin and a lamp
        /// watching a number nobody answers.</item>
        /// <item><b>Two phones cannot register as one extension.</b> That was the old rule about
        /// <c>Phones.ExtensionID</c> and it follows the registration onto the key (schema 020): two
        /// handsets signed in as 1001 both ring, and only one of them is the one anybody
        /// expected.</item>
        /// </list>
        /// </summary>
        private void ThrowIfInvalid(long phoneID, List<PhoneButton> buttons)
        {
            var errors = PhoneButton.ValidateSet(buttons);
            var extensions = new ExtensionRepository(this.database).GetAll();
            var claimed = this.GetLines().Where(line => line.PhoneID != phoneID).ToList();

            foreach (var button in buttons.Where(b => PhoneButtonTarget.IsExtension(b.TargetType)))
            {
                if (!extensions.Any(e => string.Equals(e.Number, button.TargetValue, StringComparison.Ordinal)))
                    errors.Add($"Key {button.Position}: extension {button.TargetValue} is not there any more. Choose another.");
                else if (button.IsLine && claimed.Any(line => string.Equals(line.TargetValue, button.TargetValue, StringComparison.Ordinal)))
                    errors.Add($"Key {button.Position}: another phone already registers as extension {button.TargetValue}.");
            }

            if (phoneID <= 0)
                errors.Add("Keys can only be saved against a phone that exists.");

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
