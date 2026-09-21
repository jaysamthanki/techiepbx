using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Music on hold rows (D119, D122). The interesting part is the file name: every track in a
    /// class shares one directory, so the stored name has to be unique within it, and it is derived
    /// from the row's own ID to make that true without asking the admin to care.
    ///
    /// A fresh database is not empty — the schema seeds the class that ships and the three tracks
    /// the installer puts in it — so every test but the one that checks that starts by clearing the
    /// rows out, and adds its own.
    /// </summary>
    public class MohFileRepositoryTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-moh-").FullName;
        private readonly Database database;
        private readonly MohClassRepository classes;
        private readonly MohFileRepository files;
        private readonly long mohClassID;

        public MohFileRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.classes = new MohClassRepository(this.database);
            this.files = new MohFileRepository(this.database);
            this.mohClassID = this.classes.Default()!.MohClassID;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private long Add(string name, long? classID = null) =>
            this.files.Insert(new MohFile
            {
                CreatedUnix = 1_700_000_000,
                MohClassID = classID ?? this.mohClassID,
                Name = name,
            });

        /// <summary>Removes the tracks the schema seeds, so a test can count its own.</summary>
        private void Clear()
        {
            using var connection = this.database.Open();
            connection.Execute("DELETE FROM MohFiles");
        }

        /// <summary>
        /// What a brand new system has: one class, and the three tracks the installer transcodes
        /// into its directory. They are rows so that they can be listed, renamed and deleted like
        /// anything else — the files themselves are the installer's (D122).
        /// </summary>
        [Fact]
        public void A_fresh_database_ships_a_default_class_with_three_tracks()
        {
            var shipped = this.classes.Default();

            Assert.NotNull(shipped);
            Assert.True(shipped!.IsDefault);
            Assert.Equal(MohClass.DefaultName, shipped.Name);
            Assert.Equal(MohClass.DefaultDirectory, shipped.Directory);

            var tracks = this.files.GetByClass(shipped.MohClassID);

            Assert.Equal(3, tracks.Count);
            Assert.Equal(new[] { "default-1.g722", "default-2.g722", "default-3.g722" }, tracks.Select(t => t.File));
            Assert.All(tracks, track => Assert.Empty(track.Validate()));
        }

        [Fact]
        public void A_new_track_is_named_after_its_id_and_its_name()
        {
            this.Clear();
            var id = this.Add("Piano Loop");

            Assert.Equal($"{id}-piano-loop.g722", this.files.GetByID(id)!.File);
            Assert.Equal(this.mohClassID, this.files.GetByID(id)!.MohClassID);
        }

        /// <summary>
        /// The reason the ID is in the file name at all: two names that slug the same way would
        /// otherwise be one file, and one track would silently overwrite the other.
        /// </summary>
        [Fact]
        public void Two_tracks_that_read_the_same_still_get_different_files()
        {
            this.Clear();
            var first = this.Add("Jazz'");
            var second = this.Add("jazz");

            Assert.NotEqual(this.files.GetByID(first)!.File, this.files.GetByID(second)!.File);
            Assert.EndsWith("-jazz.g722", this.files.GetByID(first)!.File);
            Assert.EndsWith("-jazz.g722", this.files.GetByID(second)!.File);
        }

        /// <summary>
        /// Two classes are two directories, so the same file name in each is two different files —
        /// which is what makes the unique constraint per class rather than global (D122).
        /// </summary>
        [Fact]
        public void The_same_track_may_exist_in_two_classes()
        {
            this.Clear();
            var other = this.classes.Insert(new MohClass { Name = "Front desk", Directory = "front-desk" });

            var first = this.Add("Piano Loop");
            var second = this.Add("Piano Loop", other);

            Assert.NotEqual(first, second);
            Assert.Single(this.files.GetByClass(this.mohClassID));
            Assert.Single(this.files.GetByClass(other));
        }

        [Fact]
        public void A_track_in_a_class_that_does_not_exist_is_refused()
        {
            this.Clear();

            Assert.Throws<ValidationFailedException>(() => this.Add("Piano Loop", 999));
            Assert.Throws<ValidationFailedException>(() => this.Add("Piano Loop", 0));
        }

        [Fact]
        public void Renaming_a_track_renames_its_file()
        {
            this.Clear();
            var id = this.Add("Piano Loop");
            var track = this.files.GetByID(id)!;

            track.Name = "Guitar Loop";
            this.files.Update(track);

            Assert.Equal($"{id}-guitar-loop.g722", this.files.GetByID(id)!.File);
        }

        /// <summary>
        /// The order the table and the conf file both use, and the order Asterisk will play them
        /// in: the class, then the file name, because a class sorts its directory alphabetically.
        /// </summary>
        [Fact]
        public void Tracks_come_back_in_the_order_asterisk_will_play_them()
        {
            this.Clear();
            var other = this.classes.Insert(new MohClass { Name = "Front desk", Directory = "front-desk" });

            this.Add("Zebra");
            this.Add("Apple");
            this.Add("Middle", other);

            Assert.Equal(
                new[] { "1-zebra.g722", "2-apple.g722", "3-middle.g722" },
                this.files.GetAll().Select(f => f.File));

            Assert.Equal(new[] { "1-zebra.g722", "2-apple.g722" }, this.files.GetByClass(this.mohClassID).Select(f => f.File));
        }

        /// <summary>The class table's Tracks column, which is one query rather than one per class.</summary>
        [Fact]
        public void The_counts_are_per_class()
        {
            this.Clear();
            var other = this.classes.Insert(new MohClass { Name = "Front desk", Directory = "front-desk" });

            this.Add("Zebra");
            this.Add("Apple");
            this.Add("Middle", other);

            var counts = this.files.CountsByClass();

            Assert.Equal(2, counts[this.mohClassID]);
            Assert.Equal(1, counts[other]);
        }

        [Fact]
        public void A_track_with_no_name_is_refused()
        {
            Assert.Throws<ValidationFailedException>(() => this.Add("   "));
        }

        [Fact]
        public void A_name_with_characters_we_would_not_write_is_refused()
        {
            Assert.Throws<ValidationFailedException>(() => this.Add("Jazz;[evil]"));
        }

        [Fact]
        public void Deleting_a_track_removes_the_row()
        {
            this.Clear();
            var id = this.Add("Piano Loop");

            this.files.Delete(id);

            Assert.Null(this.files.GetByID(id));
            Assert.Empty(this.files.GetAll());
        }

        /// <summary>
        /// Nothing points at a track, so deleting one can never be refused — unlike an
        /// announcement, which an IVR may still be greeting with (D58).
        /// </summary>
        [Fact]
        public void Deleting_a_track_that_is_not_there_is_not_an_error()
        {
            this.files.Delete(99);
        }

        /// <summary>
        /// A name with nothing usable in it still has to produce a file name, because the column
        /// is not nullable and the class's directory is flat.
        /// </summary>
        [Fact]
        public void A_name_with_no_letters_or_digits_still_gets_a_file()
        {
            this.Clear();
            var id = this.Add("...");

            Assert.Equal($"{id}-music.g722", this.files.GetByID(id)!.File);
            Assert.True(MohFile.IsValidFile(this.files.GetByID(id)!.File));
        }

        [Theory]
        [InlineData("1-piano-loop.g722")]
        [InlineData("12-a.g722")]
        // What the installer writes into the class that ships, which no ID was ever derived for.
        [InlineData("default-1.g722")]
        public void A_file_name_we_would_have_written_is_valid(string fileName)
        {
            Assert.True(MohFile.IsValidFile(fileName));
        }

        [Theory]
        [InlineData("1-Piano.g722")]
        [InlineData("1-piano.mp3")]
        [InlineData("../../etc/asterisk/pjsip.conf")]
        [InlineData("1-piano loop.g722")]
        [InlineData("sub/dir/1-piano.g722")]
        public void A_file_name_we_would_not_have_written_is_not(string fileName)
        {
            Assert.False(MohFile.IsValidFile(fileName));
        }
    }
}
