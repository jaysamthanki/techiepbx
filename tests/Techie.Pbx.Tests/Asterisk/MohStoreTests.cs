using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Where music on hold lives and what may be written there (D119, D122). The conversion itself
    /// is <c>AudioConverter</c>'s and is covered by the announcement store's tests; what is
    /// different here is the directory per class — a class plays all of its own, so a stray file is
    /// music nobody asked for, and a file in the wrong class is music the wrong caller hears.
    /// </summary>
    public class MohStoreTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-moh-store-").FullName;
        private readonly string mohPath;
        private readonly MohStore store;

        private static readonly MohClass Standard = new()
        {
            Directory = MohClass.DefaultDirectory,
            IsDefault = true,
            MohClassID = 1,
            Name = MohClass.DefaultName,
        };

        private static readonly MohClass FrontDesk = new()
        {
            Directory = "front-desk",
            MohClassID = 2,
            Name = "Front desk",
        };

        public MohStoreTests()
        {
            this.mohPath = Path.Combine(this.directory, "moh");
            Directory.CreateDirectory(this.mohPath);
            this.store = new MohStore(this.mohPath);
        }

        public void Dispose() => Directory.Delete(this.directory, recursive: true);

        private static MohFile Sample(long id = 1, string name = "Piano Loop", long mohClassID = 1) => new()
        {
            MohClassID = mohClassID,
            MohFileID = id,
            Name = name,
            File = MohFile.FileNameFor(id, name),
        };

        private string Write(MohClass mohClass, MohFile file, int bytes = 16_044)
        {
            var path = Path.Combine(this.mohPath, mohClass.Directory, file.File);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[bytes]);
            return path;
        }

        [Fact]
        public void Every_track_lives_in_its_own_class_directory()
        {
            var file = Sample();

            Assert.Equal(
                Path.Combine(this.mohPath, Standard.Directory, file.File),
                this.store.PathFor(Standard, file.MohFileID, file.File));

            Assert.Equal(
                Path.Combine(this.mohPath, FrontDesk.Directory, file.File),
                this.store.PathFor(FrontDesk, file.MohFileID, file.File));
        }

        [Theory]
        [InlineData("../../etc/asterisk/pjsip.conf")]
        [InlineData("/etc/passwd")]
        [InlineData("1-piano.g722/../../evil.g722")]
        [InlineData("1-Piano.g722")]
        public void A_file_name_we_did_not_derive_never_reaches_a_path(string fileName)
        {
            Assert.Throws<InvalidOperationException>(() => this.store.PathFor(Standard, 1, fileName));
        }

        [Theory]
        [InlineData("..")]
        [InlineData("../../etc/asterisk")]
        [InlineData("/etc")]
        [InlineData("Default")]
        public void A_class_directory_we_did_not_derive_never_reaches_a_path(string directoryName)
        {
            var mohClass = new MohClass { MohClassID = 9, Name = "Evil", Directory = directoryName };

            Assert.Throws<InvalidOperationException>(() => this.store.ClassPath(mohClass));
            Assert.Throws<InvalidOperationException>(() => this.store.PathFor(mohClass, 1, "1-piano-loop.g722"));
        }

        /// <summary>The name is derived from the ID, so there is no path to build before there is one.</summary>
        [Fact]
        public void A_track_that_has_not_been_saved_has_nowhere_to_put_its_audio()
        {
            Assert.Throws<InvalidOperationException>(() => this.store.PathFor(Standard, 0, "1-piano-loop.g722"));
        }

        [Fact]
        public void A_track_with_a_file_on_disk_describes_it()
        {
            var file = Sample();
            this.Write(Standard, file);

            var audio = this.store.Describe(Standard, file);

            Assert.NotNull(audio);
            Assert.Equal(file.File, audio!.FileName);
            Assert.Equal(1.0, audio.Seconds, 1);
        }

        /// <summary>
        /// A row and a file can drift apart, and the UI says so rather than showing a length for
        /// nothing. A file in another class's directory is one of the ways that happens.
        /// </summary>
        [Fact]
        public void A_track_whose_file_is_missing_describes_nothing()
        {
            var file = Sample();
            this.Write(Standard, file);

            Assert.Null(this.store.Describe(FrontDesk, file));
            Assert.Null(this.store.Describe(Standard, Sample(2, "Guitar Loop")));
            Assert.Null(this.store.Describe(Standard, new MohFile { MohClassID = 1, MohFileID = 1, Name = "No audio" }));
        }

        [Fact]
        public void Renaming_moves_the_file_and_leaves_nothing_behind()
        {
            var file = Sample();
            this.Write(Standard, file);

            var renamed = MohFile.FileNameFor(file.MohFileID, "Guitar Loop");
            this.store.Rename(Standard, file.MohFileID, file.File, renamed);

            Assert.False(File.Exists(Path.Combine(this.mohPath, Standard.Directory, file.File)));
            Assert.True(File.Exists(Path.Combine(this.mohPath, Standard.Directory, renamed)));
        }

        /// <summary>
        /// Changing a class's directory has to take its music with it, or the class plays an empty
        /// directory and the tracks sit where nothing looks (D122).
        /// </summary>
        [Fact]
        public void Renaming_a_class_moves_its_music()
        {
            var file = Sample();
            this.Write(Standard, file);

            var moved = new MohClass { MohClassID = 1, Name = Standard.Name, Directory = "hold-music" };
            this.store.RenameClass(Standard, moved);

            Assert.False(Directory.Exists(Path.Combine(this.mohPath, Standard.Directory)));
            Assert.True(File.Exists(Path.Combine(this.mohPath, moved.Directory, file.File)));
        }

        [Fact]
        public void Deleting_a_track_removes_its_file_and_only_its_file()
        {
            var first = Sample();
            var second = Sample(2, "Guitar Loop");
            var other = Sample(3, "Piano Loop", mohClassID: 2);
            this.Write(Standard, first);
            this.Write(Standard, second);
            this.Write(FrontDesk, other);

            this.store.Delete(Standard, first);

            Assert.False(File.Exists(Path.Combine(this.mohPath, Standard.Directory, first.File)));
            Assert.True(File.Exists(Path.Combine(this.mohPath, Standard.Directory, second.File)));
            Assert.True(File.Exists(Path.Combine(this.mohPath, FrontDesk.Directory, other.File)));
        }

        /// <summary>
        /// Deleting a class takes its whole directory, because every file in it was that class's
        /// music and nothing else names that directory.
        /// </summary>
        [Fact]
        public void Deleting_a_class_removes_its_directory_and_only_that()
        {
            this.Write(Standard, Sample());
            this.Write(FrontDesk, Sample(2, "Guitar Loop", mohClassID: 2));

            this.store.DeleteClass(FrontDesk);

            Assert.False(Directory.Exists(Path.Combine(this.mohPath, FrontDesk.Directory)));
            Assert.True(Directory.Exists(Path.Combine(this.mohPath, Standard.Directory)));
        }

        [Fact]
        public void Deleting_a_track_with_no_file_is_not_an_error()
        {
            this.store.Delete(Standard, new MohFile { MohClassID = 1, MohFileID = 1, Name = "No audio" });
            this.store.Delete(Standard, Sample());
            this.store.DeleteClass(FrontDesk);
        }

        /// <summary>
        /// The base path is a constant rather than a setting because the generated musiconhold.conf
        /// names it: the two have to agree on the directory or Asterisk plays nothing.
        /// </summary>
        [Fact]
        public void The_default_path_is_the_one_the_generated_class_names()
        {
            Assert.Equal("/var/lib/asterisk/moh", MohStore.DefaultMohPath);
            Assert.Equal(MohStore.DefaultMohPath, new MohStore().MohPath);
            Assert.Equal("/var/lib/asterisk/moh/default", MohStore.ConfDirectory(Standard));
        }
    }
}
