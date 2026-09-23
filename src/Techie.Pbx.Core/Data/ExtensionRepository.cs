using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    public class ExtensionRepository
    {
        private const string Columns =
            "ExtensionID, Number, Name, Secret, Enabled, OutboundCallerID, Forwarding, " +
            "VoicemailEnabled, VoicemailPin, VoicemailEmail, VoicemailAttachRecording, VoicemailDeleteAfterEmail, " +
            "VoicemailTranscribe";
        private const int SqliteConstraintError = 19;

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public ExtensionRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        public void Delete(long extensionID)
        {
            using var connection = this.database.Open();
            connection.Execute("DELETE FROM Extensions WHERE ExtensionID = @extensionID", new { extensionID });

            this.pending.Raise();
        }

        public List<Extension> GetAll()
        {
            using var connection = this.database.Open();
            return connection.Query<Extension>($"SELECT {Columns} FROM Extensions ORDER BY CAST(Number AS INTEGER)").ToList();
        }

        public Extension? GetByID(long extensionID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<Extension>($"SELECT {Columns} FROM Extensions WHERE ExtensionID = @extensionID", new { extensionID });
        }

        public Extension? GetByNumber(string number)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<Extension>($"SELECT {Columns} FROM Extensions WHERE Number = @number", new { number });
        }

        public long Insert(Extension extension)
        {
            this.ThrowIfInvalid(extension);

            using var connection = this.database.Open();
            try
            {
                extension.ExtensionID = connection.ExecuteScalar<long>(
                    "INSERT INTO Extensions " +
                    "(Number, Name, Secret, Enabled, OutboundCallerID, Forwarding, VoicemailEnabled, VoicemailPin, VoicemailEmail, VoicemailAttachRecording, VoicemailDeleteAfterEmail, VoicemailTranscribe) " +
                    "VALUES (@Number, @Name, @Secret, @Enabled, @OutboundCallerID, @Forwarding, @VoicemailEnabled, @VoicemailPin, @VoicemailEmail, @VoicemailAttachRecording, @VoicemailDeleteAfterEmail, @VoicemailTranscribe); " +
                    "SELECT last_insert_rowid();",
                    extension);

                this.pending.Raise();
                return extension.ExtensionID;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"Extension {extension.Number} already exists.");
            }
        }

        /// <summary>
        /// Saves the extension. A changed number takes every reference to the old one with it, in
        /// the same transaction (piece 37) — destinations, forwarding, ring group members and phone
        /// keys. The references are rewritten first, so a number another extension already has
        /// fails on the row's own UNIQUE after them and rolls the lot back.
        /// </summary>
        public void Update(Extension extension)
        {
            var stored = this.GetByID(extension.ExtensionID);
            var renumbered = stored != null && !string.Equals(stored.Number, extension.Number, StringComparison.Ordinal);

            // Its own forwarding list is the one reference the sweep cannot reach, because this row
            // is about to be written from the object in hand: "keep my own handset ringing" has to
            // follow the new number rather than start ringing whoever takes the old one (D130).
            if (renumbered && extension.ForwardingList().Contains(stored!.Number, StringComparer.Ordinal))
            {
                extension.Forwarding = string.Join(" ", extension.ForwardingList()
                    .Select(t => string.Equals(t, stored.Number, StringComparison.Ordinal) ? extension.Number : t));
            }

            this.ThrowIfInvalid(extension);

            using var connection = this.database.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                if (renumbered)
                    Renumbering.Extension(connection, transaction, stored!.Number, extension.Number);

                var rows = connection.Execute(
                    "UPDATE Extensions SET " +
                    "Number = @Number, Name = @Name, Secret = @Secret, Enabled = @Enabled, " +
                    "OutboundCallerID = @OutboundCallerID, Forwarding = @Forwarding, " +
                    "VoicemailEnabled = @VoicemailEnabled, VoicemailPin = @VoicemailPin, VoicemailEmail = @VoicemailEmail, " +
                    "VoicemailAttachRecording = @VoicemailAttachRecording, VoicemailDeleteAfterEmail = @VoicemailDeleteAfterEmail, " +
                    "VoicemailTranscribe = @VoicemailTranscribe " +
                    "WHERE ExtensionID = @ExtensionID",
                    extension, transaction);
                if (rows == 0)
                    throw new ValidationFailedException($"ExtensionID {extension.ExtensionID} does not exist.");

                transaction.Commit();
                this.pending.Raise();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"Extension {extension.Number} already exists.");
            }
        }

        /// <summary>
        /// The model's own rules, plus the one that needs the other rows: a forwarding target that
        /// looks like an extension number has to be an extension that exists and is switched on
        /// (D130).
        ///
        /// Short numbers are extensions here, which is the rule dialling from a phone already
        /// follows — the internal context matches the extensions before it tries an outbound route
        /// — so a mistyped 104 is a mistake to report rather than a number to send to a provider.
        /// An extension's own number is deliberately allowed: that is how somebody keeps their own
        /// handset in the ring while adding a mobile to it.
        /// </summary>
        private void ThrowIfInvalid(Extension extension)
        {
            var errors = extension.Validate();

            var targets = extension.ForwardingList()
                .Where(t => Extension.IsForwardingTarget(t) && Extension.IsValidNumber(t))
                .Where(t => !string.Equals(t, extension.Number, StringComparison.Ordinal))
                .ToList();

            if (targets.Count > 0)
            {
                var all = this.GetAll();

                foreach (var target in targets)
                {
                    var other = all.FirstOrDefault(e => string.Equals(e.Number, target, StringComparison.Ordinal));

                    if (other == null)
                        errors.Add($"There is no extension {target} to forward to.");
                    else if (!other.Enabled)
                        errors.Add($"Extension {target} is disabled, so it would never ring.");
                }
            }

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
