using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// Announcement rows. The audio file itself is not this class's business: it lives on disk and
    /// is written by <c>AnnouncementStore</c> (D55). What is stored here is what the file is
    /// called, so that renaming an announcement and losing track of its file are two different
    /// problems rather than one.
    /// </summary>
    public class AnnouncementRepository
    {
        private const string Columns =
            "AnnouncementID, Name, Description, PlayExtension, AudioFile, Enabled";

        private const int SqliteConstraintError = 19;

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public AnnouncementRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        public void Delete(long announcementID)
        {
            using var connection = this.database.Open();

            try
            {
                connection.Execute("DELETE FROM Announcements WHERE AnnouncementID = @announcementID", new { announcementID });
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                // An IVR still greets with it (D58). Deleting the menu as well would be a
                // surprise; saying so is not.
                throw new ValidationFailedException(
                    "This announcement is still an IVR's greeting. Point the IVR at another announcement first.");
            }

            this.pending.Raise();
        }

        public List<Announcement> GetAll()
        {
            using var connection = this.database.Open();
            return connection.Query<Announcement>($"SELECT {Columns} FROM Announcements ORDER BY Name").ToList();
        }

        public Announcement? GetByID(long announcementID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<Announcement>(
                $"SELECT {Columns} FROM Announcements WHERE AnnouncementID = @announcementID", new { announcementID });
        }

        public long Insert(Announcement announcement)
        {
            this.ThrowIfInvalid(announcement);

            using var connection = this.database.Open();
            try
            {
                announcement.AnnouncementID = connection.ExecuteScalar<long>(
                    "INSERT INTO Announcements (Name, Description, PlayExtension, AudioFile, Enabled) " +
                    "VALUES (@Name, @Description, @PlayExtension, @AudioFile, @Enabled); " +
                    "SELECT last_insert_rowid();",
                    announcement);

                this.pending.Raise();
                return announcement.AnnouncementID;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"An announcement called '{announcement.Name}' already exists.");
            }
        }

        public void Update(Announcement announcement)
        {
            this.ThrowIfInvalid(announcement);

            using var connection = this.database.Open();
            try
            {
                var rows = connection.Execute(
                    "UPDATE Announcements SET Name = @Name, Description = @Description, " +
                    "PlayExtension = @PlayExtension, AudioFile = @AudioFile, Enabled = @Enabled " +
                    "WHERE AnnouncementID = @AnnouncementID",
                    announcement);
                if (rows == 0)
                    throw new ValidationFailedException($"AnnouncementID {announcement.AnnouncementID} does not exist.");

                this.pending.Raise();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"An announcement called '{announcement.Name}' already exists.");
            }
        }

        /// <summary>
        /// The model's own rules, plus the one that needs the database: a play extension is dialled
        /// out of the same context as everything else, so it has to be a number nothing else has
        /// already claimed (D57). Feature codes are not checked because they all start with '*'
        /// and a play extension is digits only, so the two cannot meet.
        /// </summary>
        private void ThrowIfInvalid(Announcement announcement)
        {
            var errors = announcement.Validate();

            if (announcement.PlayExtension.Length > 0)
            {
                var number = announcement.PlayExtension;

                if (new ExtensionRepository(this.database).GetAll()
                    .Any(e => string.Equals(e.Number, number, StringComparison.Ordinal)))
                    errors.Add($"Extension {number} already uses that number.");

                if (new RingGroupRepository(this.database).GetAll()
                    .Any(g => string.Equals(g.Number, number, StringComparison.Ordinal)))
                    errors.Add($"Ring group {number} already uses that number.");

                var ivr = new IvrRepository(this.database).GetAll()
                    .FirstOrDefault(i => string.Equals(i.PlayExtension, number, StringComparison.Ordinal));

                if (ivr != null)
                    errors.Add($"IVR '{ivr.Name}' already plays on {number}.");

                var clash = this.GetAll().FirstOrDefault(a =>
                    a.AnnouncementID != announcement.AnnouncementID &&
                    string.Equals(a.PlayExtension, number, StringComparison.Ordinal));

                if (clash != null)
                    errors.Add($"Announcement '{clash.Name}' already plays on {number}.");
            }

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
