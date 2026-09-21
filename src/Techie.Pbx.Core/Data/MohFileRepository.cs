using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// Music on hold rows (D119, D122). The audio itself is not this class's business: it lives on
    /// disk and is written by <c>MohStore</c>, exactly as an announcement's is (D55). What is
    /// stored here is what each track is called, what its file is called, and which music on hold
    /// class — which is to say which directory — it belongs to.
    ///
    /// Nothing points at a track, so there is no reference to check before deleting one — the only
    /// thing that reads these rows is the generated musiconhold.conf, which is a list of what is in
    /// the directory.
    /// </summary>
    public class MohFileRepository
    {
        private const string Columns = "MohFileID, MohClassID, Name, File, CreatedUnix";

        private const int SqliteConstraintError = 19;

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public MohFileRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        public void Delete(long mohFileID)
        {
            using var connection = this.database.Open();
            connection.Execute("DELETE FROM MohFiles WHERE MohFileID = @mohFileID", new { mohFileID });

            this.pending.Raise();
        }

        /// <summary>
        /// How many tracks each class has, by MohClassID. One query rather than one per class, for
        /// the table that lists the classes — a class with no tracks is not in the answer.
        /// </summary>
        public Dictionary<long, int> CountsByClass()
        {
            using var connection = this.database.Open();
            return connection.Query<ClassCount>(
                    "SELECT MohClassID, COUNT(*) AS Tracks FROM MohFiles GROUP BY MohClassID")
                .ToDictionary(row => row.MohClassID, row => row.Tracks);
        }

        /// <summary>
        /// Every track of every class, in the order Asterisk will play them within each class. That
        /// order is the file name, not the display name, because musiconhold.conf sorts a class's
        /// directory alphabetically (D119) — so the table an admin reads is in the same order as
        /// the music they will hear.
        /// </summary>
        public List<MohFile> GetAll()
        {
            using var connection = this.database.Open();
            return connection.Query<MohFile>($"SELECT {Columns} FROM MohFiles ORDER BY MohClassID, File").ToList();
        }

        /// <summary>The tracks of one class, in the order that class will play them.</summary>
        public List<MohFile> GetByClass(long mohClassID)
        {
            using var connection = this.database.Open();
            return connection.Query<MohFile>(
                $"SELECT {Columns} FROM MohFiles WHERE MohClassID = @mohClassID ORDER BY File",
                new { mohClassID }).ToList();
        }

        public MohFile? GetByID(long mohFileID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<MohFile>(
                $"SELECT {Columns} FROM MohFiles WHERE MohFileID = @mohFileID", new { mohFileID });
        }

        /// <summary>
        /// Inserts the row, fills in the file name it derives from the new ID, and returns that
        /// ID. The two statements are one transaction because File is unique within the class and
        /// the row cannot name its file until it has an ID: doing it in two goes would leave a row
        /// whose File was briefly empty, and a second one could not then be inserted at all.
        /// </summary>
        public long Insert(MohFile file)
        {
            ThrowIfInvalid(file);

            using var connection = this.database.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                file.MohFileID = connection.ExecuteScalar<long>(
                    "INSERT INTO MohFiles (MohClassID, Name, File, CreatedUnix) " +
                    "VALUES (@MohClassID, @Name, '', @CreatedUnix); " +
                    "SELECT last_insert_rowid();",
                    file, transaction);

                file.File = MohFile.FileNameFor(file.MohFileID, file.Name);

                connection.Execute(
                    "UPDATE MohFiles SET File = @File WHERE MohFileID = @MohFileID", file, transaction);

                transaction.Commit();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException(Refused(file, ex));
            }

            this.pending.Raise();
            return file.MohFileID;
        }

        /// <summary>
        /// Renames the track, which renames its file: the stored name is derived from the ID and
        /// the display name, so the two can never say different things. The file on disk is
        /// <c>MohStore</c>'s to move — this only says what it should be called.
        /// </summary>
        public void Update(MohFile file)
        {
            file.File = MohFile.FileNameFor(file.MohFileID, file.Name);
            ThrowIfInvalid(file);

            using var connection = this.database.Open();
            try
            {
                var rows = connection.Execute(
                    "UPDATE MohFiles SET Name = @Name, File = @File WHERE MohFileID = @MohFileID", file);
                if (rows == 0)
                    throw new ValidationFailedException($"MohFileID {file.MohFileID} does not exist.");

                this.pending.Raise();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException(Refused(file, ex));
            }
        }

        /// <summary>
        /// Why the database refused the row, in the admin's words. Two constraints can fail here:
        /// the class the track names is not there any more, or the class already holds a track
        /// stored under that file name.
        /// </summary>
        private static string Refused(MohFile file, SqliteException ex) =>
            ex.Message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
                ? "That music on hold class does not exist any more."
                : $"That class already has a track stored as '{file.File}'.";

        private static void ThrowIfInvalid(MohFile file)
        {
            var errors = file.Validate();

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }

        /// <summary>One row of <see cref="CountsByClass"/>: a class and how many tracks it has.</summary>
        private class ClassCount
        {
            public long MohClassID { get; set; }

            public int Tracks { get; set; }
        }
    }
}
