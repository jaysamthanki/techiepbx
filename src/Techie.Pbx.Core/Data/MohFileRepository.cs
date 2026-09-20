using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// Music on hold rows (D119). The audio itself is not this class's business: it lives on disk
    /// and is written by <c>MohStore</c>, exactly as an announcement's is (D55). What is stored
    /// here is what each track is called and what its file is called.
    ///
    /// Nothing points at a track, so there is no reference to check before deleting one — the only
    /// thing that reads these rows is the generated musiconhold.conf, which is a list of what is in
    /// the directory.
    /// </summary>
    public class MohFileRepository
    {
        private const string Columns = "MohFileID, Name, File, CreatedUnix";

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
        /// Every track, in the order Asterisk will play them. That order is the file name, not the
        /// display name, because musiconhold.conf sorts the directory alphabetically (D119) — so
        /// the table an admin reads is in the same order as the music they will hear.
        /// </summary>
        public List<MohFile> GetAll()
        {
            using var connection = this.database.Open();
            return connection.Query<MohFile>($"SELECT {Columns} FROM MohFiles ORDER BY File").ToList();
        }

        public MohFile? GetByID(long mohFileID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<MohFile>(
                $"SELECT {Columns} FROM MohFiles WHERE MohFileID = @mohFileID", new { mohFileID });
        }

        /// <summary>
        /// Inserts the row, fills in the file name it derives from the new ID, and returns that
        /// ID. The two statements are one transaction because File is UNIQUE and the row cannot
        /// name its file until it has an ID: doing it in two goes would leave a row whose File was
        /// briefly empty, and a second one could not then be inserted at all.
        /// </summary>
        public long Insert(MohFile file)
        {
            ThrowIfInvalid(file);

            using var connection = this.database.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                file.MohFileID = connection.ExecuteScalar<long>(
                    "INSERT INTO MohFiles (Name, File, CreatedUnix) VALUES (@Name, '', @CreatedUnix); " +
                    "SELECT last_insert_rowid();",
                    file, transaction);

                file.File = MohFile.FileNameFor(file.MohFileID, file.Name);

                connection.Execute(
                    "UPDATE MohFiles SET File = @File WHERE MohFileID = @MohFileID", file, transaction);

                transaction.Commit();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A music on hold track is already stored as '{file.File}'.");
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
                throw new ValidationFailedException($"A music on hold track is already stored as '{file.File}'.");
            }
        }

        private static void ThrowIfInvalid(MohFile file)
        {
            var errors = file.Validate();

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
