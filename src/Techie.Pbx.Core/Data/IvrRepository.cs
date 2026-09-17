using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// IVR rows and their digit maps. The two are always read and written together — a menu with
    /// no keys is not a menu — so an <see cref="Ivr"/> carries its entries and a save replaces
    /// them wholesale inside one transaction (D59).
    /// </summary>
    public class IvrRepository
    {
        private const string Columns =
            "IvrID, Name, Description, AnnouncementID, PlayExtension, TimeoutSeconds, Retries, " +
            "EnableDirectDial, DestinationType, DestinationValue, Enabled";

        private const string EntryColumns =
            "IvrEntryID, IvrID, Digit, DestinationType, DestinationValue";

        private const int SqliteConstraintError = 19;

        /// <summary>
        /// How far a chain of "nobody chose anything, go there" is followed before it is called a
        /// loop. The same limit ring groups use, for the same reason (D54).
        /// </summary>
        private const int MaxChain = 10;

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public IvrRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        public void Delete(long ivrID)
        {
            using var connection = this.database.Open();

            // The entries go with it: IvrEntries.IvrID is ON DELETE CASCADE, because a key has no
            // life of its own once the menu it belongs to is gone.
            connection.Execute("DELETE FROM Ivrs WHERE IvrID = @ivrID", new { ivrID });

            this.pending.Raise();
        }

        public List<Ivr> GetAll()
        {
            using var connection = this.database.Open();

            var ivrs = connection.Query<Ivr>($"SELECT {Columns} FROM Ivrs ORDER BY Name").ToList();
            var entries = connection.Query<IvrEntry>($"SELECT {EntryColumns} FROM IvrEntries").ToList();

            foreach (var ivr in ivrs)
                ivr.Entries = InMenuOrder(entries.Where(e => e.IvrID == ivr.IvrID));

            return ivrs;
        }

        public Ivr? GetByID(long ivrID)
        {
            using var connection = this.database.Open();

            var ivr = connection.QuerySingleOrDefault<Ivr>($"SELECT {Columns} FROM Ivrs WHERE IvrID = @ivrID", new { ivrID });
            if (ivr == null)
                return null;

            ivr.Entries = InMenuOrder(connection.Query<IvrEntry>(
                $"SELECT {EntryColumns} FROM IvrEntries WHERE IvrID = @ivrID", new { ivrID }));

            return ivr;
        }

        public long Insert(Ivr ivr)
        {
            this.ThrowIfInvalid(ivr);

            using var connection = this.database.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                ivr.IvrID = connection.ExecuteScalar<long>(
                    "INSERT INTO Ivrs (Name, Description, AnnouncementID, PlayExtension, TimeoutSeconds, Retries, " +
                    "EnableDirectDial, DestinationType, DestinationValue, Enabled) " +
                    "VALUES (@Name, @Description, @AnnouncementID, @PlayExtension, @TimeoutSeconds, @Retries, " +
                    "@EnableDirectDial, @DestinationType, @DestinationValue, @Enabled); " +
                    "SELECT last_insert_rowid();",
                    ivr, transaction);

                WriteEntries(connection, transaction, ivr);
                transaction.Commit();

                this.pending.Raise();
                return ivr.IvrID;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException(Conflict(ivr, ex));
            }
        }

        public void Update(Ivr ivr)
        {
            this.ThrowIfInvalid(ivr);

            using var connection = this.database.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var rows = connection.Execute(
                    "UPDATE Ivrs SET Name = @Name, Description = @Description, AnnouncementID = @AnnouncementID, " +
                    "PlayExtension = @PlayExtension, TimeoutSeconds = @TimeoutSeconds, Retries = @Retries, " +
                    "EnableDirectDial = @EnableDirectDial, DestinationType = @DestinationType, " +
                    "DestinationValue = @DestinationValue, Enabled = @Enabled WHERE IvrID = @IvrID",
                    ivr, transaction);
                if (rows == 0)
                    throw new ValidationFailedException($"IvrID {ivr.IvrID} does not exist.");

                // The digit map is replaced rather than merged: the form posts the whole menu, and
                // working out which key was edited, added or removed would be three code paths
                // where one will do.
                connection.Execute("DELETE FROM IvrEntries WHERE IvrID = @IvrID", ivr, transaction);
                WriteEntries(connection, transaction, ivr);
                transaction.Commit();

                this.pending.Raise();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException(Conflict(ivr, ex));
            }
        }

        /// <summary>
        /// Which unique or foreign key a write fell foul of. The name is the one an admin can
        /// actually collide with; the greeting is the one they can delete out from under it.
        /// </summary>
        private static string Conflict(Ivr ivr, SqliteException ex) =>
            ex.Message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
                ? "That greeting announcement does not exist any more. Choose another."
                : $"An IVR called '{ivr.Name}' already exists.";

        /// <summary>The digit map in the order a menu reads: 0-9, then * and #.</summary>
        private static List<IvrEntry> InMenuOrder(IEnumerable<IvrEntry> entries) =>
            entries.OrderBy(e => IvrEntry.Rank(e.Digit)).ToList();

        /// <summary>
        /// Follows "nobody chose anything, go there" from this IVR and answers whether it comes
        /// back round to where it started. A caller who says nothing would otherwise be passed
        /// between menus for ever (D54, D59).
        ///
        /// Only the final destination is followed. A <b>key</b> that points back at the same menu
        /// is a caller asking to hear it again, which is a feature rather than a loop: it only
        /// happens when someone presses it.
        /// </summary>
        private static bool LoopsBack(Ivr ivr, List<Ivr> all)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { ivr.PlayExtension };
            var next = ivr.ToFinalDestination();

            for (var hop = 0; hop < MaxChain; hop++)
            {
                if (next.Type != DestinationType.Ivr)
                    return false;

                if (!seen.Add(next.Value))
                    return true;

                // The IVR being saved is the one in hand, not the older copy in the list.
                var target = string.Equals(next.Value, ivr.PlayExtension, StringComparison.Ordinal)
                    ? ivr
                    : all.FirstOrDefault(i => string.Equals(i.PlayExtension, next.Value, StringComparison.Ordinal));

                if (target == null)
                    return false;

                next = target.ToFinalDestination();
            }

            return true;
        }

        private static void WriteEntries(SqliteConnection connection, SqliteTransaction transaction, Ivr ivr)
        {
            foreach (var entry in ivr.Entries)
            {
                entry.IvrID = ivr.IvrID;
                connection.Execute(
                    "INSERT INTO IvrEntries (IvrID, Digit, DestinationType, DestinationValue) " +
                    "VALUES (@IvrID, @Digit, @DestinationType, @DestinationValue)",
                    entry, transaction);
            }
        }

        /// <summary>
        /// The model's own rules, plus the ones that need the database: the greeting has to be an
        /// announcement with audio, the play extension has to be a number nothing else has claimed
        /// (D57), every destination — the final one and every key — has to still be somewhere a
        /// call can be sent (D35), and the menu must not send a silent caller round in a circle.
        /// </summary>
        private void ThrowIfInvalid(Ivr ivr)
        {
            var errors = ivr.Validate();
            var extensions = new ExtensionRepository(this.database).GetAll();
            var announcements = new AnnouncementRepository(this.database).GetAll();
            var ringGroups = new RingGroupRepository(this.database).GetAll();
            var all = this.GetAll();

            var greeting = announcements.FirstOrDefault(a => a.AnnouncementID == ivr.AnnouncementID);
            if (greeting == null)
                errors.Add("Choose an announcement for the greeting.");
            else if (!greeting.HasAudio)
                errors.Add($"Announcement '{greeting.Name}' has no audio yet, so there would be nothing to play.");
            else if (!greeting.Enabled)
                errors.Add($"Announcement '{greeting.Name}' is switched off, so the menu would have no greeting.");

            if (ivr.PlayExtension.Length > 0)
            {
                var number = ivr.PlayExtension;

                if (extensions.Any(e => string.Equals(e.Number, number, StringComparison.Ordinal)))
                    errors.Add($"Extension {number} already uses that number.");

                if (ringGroups.Any(g => string.Equals(g.Number, number, StringComparison.Ordinal)))
                    errors.Add($"Ring group {number} already uses that number.");

                var announcement = announcements.FirstOrDefault(a =>
                    string.Equals(a.PlayExtension, number, StringComparison.Ordinal));

                if (announcement != null)
                    errors.Add($"Announcement '{announcement.Name}' already plays on {number}.");

                var condition = new TimeConditionRepository(this.database).GetAll()
                    .FirstOrDefault(t => string.Equals(t.PlayExtension, number, StringComparison.Ordinal));

                if (condition != null)
                    errors.Add($"Time condition '{condition.Name}' already uses {number}.");

                var clash = all.FirstOrDefault(i =>
                    i.IvrID != ivr.IvrID && string.Equals(i.PlayExtension, number, StringComparison.Ordinal));

                if (clash != null)
                    errors.Add($"IVR '{clash.Name}' already plays on {number}.");
            }

            if (errors.Count == 0)
            {
                // The IVR being saved is part of the world its own keys can point at: a menu whose
                // key 9 repeats the menu is allowed, and is the only reason this list includes it.
                var others = all.Where(i => i.IvrID != ivr.IvrID).Append(ivr).ToList();
                var conditions = new TimeConditionRepository(this.database).GetAll();

                if (DestinationCatalog.Find(extensions, ringGroups, announcements, others, conditions, ivr.ToFinalDestination()) == null)
                    errors.Add("That destination is not there any more. Choose another.");

                foreach (var entry in ivr.Entries)
                {
                    if (DestinationCatalog.Find(extensions, ringGroups, announcements, others, conditions, entry.ToDestination()) == null)
                        errors.Add($"Where key {entry.Digit} goes is not there any more. Choose another.");
                }

                if (LoopsBack(ivr, others))
                    errors.Add("That destination comes back round to this menu, so a caller who presses nothing would never get anywhere.");
            }

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
