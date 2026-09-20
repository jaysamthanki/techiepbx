using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Music on hold rows (D119). The interesting part is the file name: every track shares one
    /// flat directory, so the stored name has to be unique, and it is derived from the row's own
    /// ID to make that true without asking the admin to care.
    /// </summary>
    public class MohFileRepositoryTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-moh-").FullName;
        private readonly Database database;
        private readonly MohFileRepository files;

        public MohFileRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.files = new MohFileRepository(this.database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private long Add(string name) =>
            this.files.Insert(new MohFile { Name = name, CreatedUnix = 1_700_000_000 });

        [Fact]
        public void A_new_track_is_named_after_its_id_and_its_name()
        {
            var id = this.Add("Piano Loop");

            Assert.Equal($"{id}-piano-loop.wav", this.files.GetByID(id)!.File);
        }

        /// <summary>
        /// The reason the ID is in the file name at all: two names that slug the same way would
        /// otherwise be one file, and one track would silently overwrite the other.
        /// </summary>
        [Fact]
        public void Two_tracks_that_read_the_same_still_get_different_files()
        {
            var first = this.Add("Jazz'");
            var second = this.Add("jazz");

            Assert.NotEqual(this.files.GetByID(first)!.File, this.files.GetByID(second)!.File);
            Assert.EndsWith("-jazz.wav", this.files.GetByID(first)!.File);
            Assert.EndsWith("-jazz.wav", this.files.GetByID(second)!.File);
        }

        [Fact]
        public void Renaming_a_track_renames_its_file()
        {
            var id = this.Add("Piano Loop");
            var track = this.files.GetByID(id)!;

            track.Name = "Guitar Loop";
            this.files.Update(track);

            Assert.Equal($"{id}-guitar-loop.wav", this.files.GetByID(id)!.File);
        }

        /// <summary>
        /// The order the table and the conf file both use, and the order Asterisk will play them
        /// in: the file name, because the class sorts its directory alphabetically.
        /// </summary>
        [Fact]
        public void Tracks_come_back_in_the_order_asterisk_will_play_them()
        {
            this.Add("Zebra");
            this.Add("Apple");

            Assert.Equal(new[] { "1-zebra.wav", "2-apple.wav" }, this.files.GetAll().Select(f => f.File));
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
        /// is not nullable and the directory is flat.
        /// </summary>
        [Fact]
        public void A_name_with_no_letters_or_digits_still_gets_a_file()
        {
            var id = this.Add("...");

            Assert.Equal($"{id}-music.wav", this.files.GetByID(id)!.File);
            Assert.True(MohFile.IsValidFile(this.files.GetByID(id)!.File));
        }

        [Theory]
        [InlineData("1-piano-loop.wav")]
        [InlineData("12-a.wav")]
        public void A_file_name_we_would_have_written_is_valid(string fileName)
        {
            Assert.True(MohFile.IsValidFile(fileName));
        }

        [Theory]
        [InlineData("piano-loop.wav")]
        [InlineData("1-Piano.wav")]
        [InlineData("1-piano.mp3")]
        [InlineData("../../etc/asterisk/pjsip.conf")]
        [InlineData("1-piano loop.wav")]
        public void A_file_name_we_would_not_have_written_is_not(string fileName)
        {
            Assert.False(MohFile.IsValidFile(fileName));
        }
    }
}
