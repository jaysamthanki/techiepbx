using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// The music on hold classes (D122). A class is a name Asterisk knows and a directory it plays;
    /// the directory itself, and the audio in it, are <c>MohStore</c>'s business, exactly as an
    /// announcement's audio is.
    ///
    /// Deleting a class deletes its tracks' rows with it — the foreign key cascades, because a
    /// track outside a class is a file nothing would ever play. The class that ships cannot be
    /// deleted at all: it is where the installer puts the tracks that come with the product.
    /// </summary>
    public class MohClassRepository
    {
        private const string Columns = "MohClassID, Name, Directory, IsDefault";

        private const int SqliteConstraintError = 19;

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public MohClassRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        /// <summary>
        /// The class that ships with the product, or null on a database that somehow has none. It
        /// is what a class picker falls back to and what the installer's tracks live in.
        /// </summary>
        public MohClass? Default()
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<MohClass>(
                $"SELECT {Columns} FROM MohClasses WHERE IsDefault = 1 ORDER BY MohClassID LIMIT 1");
        }

        /// <summary>
        /// Removes the class, and with it the rows of every track in it. The files on disk are the
        /// caller's to remove, the way a deleted track's file is: this only owns rows.
        /// </summary>
        public void Delete(long mohClassID)
        {
            var mohClass = this.GetByID(mohClassID);
            if (mohClass == null)
                return;

            if (mohClass.IsDefault)
                throw new ValidationFailedException(
                    $"'{mohClass.Name}' is the class that ships with the system and cannot be deleted.");

            using var connection = this.database.Open();
            connection.Execute("DELETE FROM MohClasses WHERE MohClassID = @mohClassID", new { mohClassID });

            this.pending.Raise();
        }

        /// <summary>
        /// Every class, the one that ships first and the rest by name — which is the order the
        /// table lists them in and the order a class picker offers them.
        /// </summary>
        public List<MohClass> GetAll()
        {
            using var connection = this.database.Open();
            return connection.Query<MohClass>(
                $"SELECT {Columns} FROM MohClasses ORDER BY IsDefault DESC, Name COLLATE NOCASE").ToList();
        }

        public MohClass? GetByID(long mohClassID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<MohClass>(
                $"SELECT {Columns} FROM MohClasses WHERE MohClassID = @mohClassID", new { mohClassID });
        }

        /// <summary>
        /// The class of that name, matched the way Asterisk matches it: without regard to case
        /// (res/res_musiconhold.c, moh_class_cmp). Used to check that a setting naming a class is
        /// naming one that exists.
        /// </summary>
        public MohClass? GetByName(string name)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<MohClass>(
                $"SELECT {Columns} FROM MohClasses WHERE Name = @name COLLATE NOCASE", new { name = name.Trim() });
        }

        public long Insert(MohClass mohClass)
        {
            ThrowIfInvalid(mohClass);

            using var connection = this.database.Open();

            try
            {
                mohClass.MohClassID = connection.ExecuteScalar<long>(
                    "INSERT INTO MohClasses (Name, Directory, IsDefault) VALUES (@Name, @Directory, @IsDefault); " +
                    "SELECT last_insert_rowid();",
                    mohClass);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException(Taken(mohClass, ex));
            }

            this.pending.Raise();
            return mohClass.MohClassID;
        }

        /// <summary>
        /// Renames the class or moves it to another directory. Whether the class ships with the
        /// product is not something an edit may change: the flag says which directory the installer
        /// fills, and two of them — or none — would leave that installer with nowhere to write.
        /// </summary>
        public void Update(MohClass mohClass)
        {
            ThrowIfInvalid(mohClass);

            using var connection = this.database.Open();

            try
            {
                var rows = connection.Execute(
                    "UPDATE MohClasses SET Name = @Name, Directory = @Directory WHERE MohClassID = @MohClassID",
                    mohClass);

                if (rows == 0)
                    throw new ValidationFailedException($"MohClassID {mohClass.MohClassID} does not exist.");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException(Taken(mohClass, ex));
            }

            this.pending.Raise();
        }

        /// <summary>Which of the two unique columns the database refused, in the admin's words.</summary>
        private static string Taken(MohClass mohClass, SqliteException ex) =>
            ex.Message.Contains("Directory", StringComparison.OrdinalIgnoreCase)
                ? $"Another music on hold class already plays the directory '{mohClass.Directory}'."
                : $"There is already a music on hold class called '{mohClass.Name}'.";

        private static void ThrowIfInvalid(MohClass mohClass)
        {
            var errors = mohClass.Validate();

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
