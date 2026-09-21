using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Music on hold classes (D122): a name Asterisk knows and a directory it plays. What is worth
    /// testing is what a class may be called — Asterisk matches names without regard to case, and
    /// keeps one name for itself — and what happens to the tracks when a class goes.
    /// </summary>
    public class MohClassRepositoryTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-moh-classes-").FullName;
        private readonly Database database;
        private readonly MohClassRepository classes;
        private readonly MohFileRepository files;

        public MohClassRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.classes = new MohClassRepository(this.database);
            this.files = new MohFileRepository(this.database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private long Add(string name, string directoryName) =>
            this.classes.Insert(new MohClass { Name = name, Directory = directoryName });

        [Fact]
        public void The_class_that_ships_is_there_and_is_the_default_one()
        {
            var all = this.classes.GetAll();

            Assert.Single(all);
            Assert.Equal(MohClass.DefaultName, all[0].Name);
            Assert.Equal(MohClass.DefaultDirectory, all[0].Directory);
            Assert.True(all[0].IsDefault);
        }

        /// <summary>
        /// It is the directory the installer writes into and what a system nobody has configured
        /// plays, so there is no version of deleting it that leaves something that works.
        /// </summary>
        [Fact]
        public void The_class_that_ships_cannot_be_deleted()
        {
            var shipped = this.classes.Default()!;

            Assert.Throws<ValidationFailedException>(() => this.classes.Delete(shipped.MohClassID));
            Assert.NotNull(this.classes.GetByID(shipped.MohClassID));
        }

        /// <summary>
        /// Asterisk compares class names with strcasecmp, so "Jazz" and "jazz" would be one class
        /// to it: the database refuses the second rather than leaving an admin with two rows and
        /// one class (res/res_musiconhold.c, moh_class_cmp).
        /// </summary>
        [Fact]
        public void Two_classes_cannot_share_a_name_even_in_another_case()
        {
            this.Add("Jazz", "jazz");

            Assert.Throws<ValidationFailedException>(() => this.Add("JAZZ", "jazz-2"));
        }

        [Fact]
        public void Two_classes_cannot_share_a_directory()
        {
            this.Add("Jazz", "jazz");

            Assert.Throws<ValidationFailedException>(() => this.Add("Blues", "jazz"));
        }

        /// <summary>
        /// The one name Asterisk keeps for itself: it plays a class called "default" whenever music
        /// is asked for and none was named, which is exactly how a parked caller gets silence here
        /// (D119).
        /// </summary>
        [Theory]
        [InlineData("default")]
        [InlineData("Default")]
        public void A_class_cannot_be_called_default(string name)
        {
            Assert.Throws<ValidationFailedException>(() => this.Add(name, "somewhere"));
        }

        [Theory]
        [InlineData("Front Desk")]
        [InlineData("../etc")]
        [InlineData("front desk")]
        [InlineData("")]
        [InlineData("a-directory-name-far-too-long-to-store")]
        public void A_directory_we_would_not_have_written_is_refused(string directoryName)
        {
            Assert.Throws<ValidationFailedException>(() => this.Add("Front desk", directoryName));
        }

        [Fact]
        public void A_class_can_be_renamed_and_moved()
        {
            var id = this.Add("Front desk", "front-desk");
            var mohClass = this.classes.GetByID(id)!;

            mohClass.Name = "Reception";
            mohClass.Directory = "reception";
            this.classes.Update(mohClass);

            Assert.Equal("Reception", this.classes.GetByID(id)!.Name);
            Assert.Equal("reception", this.classes.GetByID(id)!.Directory);
        }

        /// <summary>
        /// A track outside a class is a file nothing would ever play, so the rows go with the class
        /// through the foreign key. The files on disk are the page's to remove.
        /// </summary>
        [Fact]
        public void Deleting_a_class_takes_its_tracks_with_it()
        {
            var id = this.Add("Front desk", "front-desk");
            this.files.Insert(new MohFile { CreatedUnix = 1, MohClassID = id, Name = "Hold Message" });

            Assert.Single(this.files.GetByClass(id));

            this.classes.Delete(id);

            Assert.Null(this.classes.GetByID(id));
            Assert.Empty(this.files.GetByClass(id));
        }

        /// <summary>The name is matched the way Asterisk matches it, so a setting can be checked.</summary>
        [Fact]
        public void A_class_is_found_by_name_whatever_the_case()
        {
            var id = this.Add("Front desk", "front-desk");

            Assert.Equal(id, this.classes.GetByName("FRONT DESK")!.MohClassID);
            Assert.Null(this.classes.GetByName("Back desk"));
        }

        /// <summary>
        /// "Front Desk" becomes "front-desk": what the form offers when an admin has only typed a
        /// name, so nobody has to invent a path.
        /// </summary>
        [Theory]
        [InlineData("Front Desk", "front-desk")]
        [InlineData("Jazz!", "jazz")]
        [InlineData("...", "music")]
        public void A_directory_is_suggested_from_the_name(string name, string expected)
        {
            Assert.Equal(expected, MohClass.DirectoryFor(name));
            Assert.True(MohClass.IsValidDirectory(MohClass.DirectoryFor(name)));
        }
    }
}
