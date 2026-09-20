using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Where music on hold lives and what may be written there (D119). The conversion itself is
    /// <c>AudioConverter</c>'s and is covered by the announcement store's tests; what is different
    /// here is the flat directory — one class plays all of it, so a stray file is music nobody
    /// asked for.
    /// </summary>
    public class MohStoreTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-moh-store-").FullName;
        private readonly string mohPath;
        private readonly MohStore store;

        public MohStoreTests()
        {
            this.mohPath = Path.Combine(this.directory, "moh");
            Directory.CreateDirectory(this.mohPath);
            this.store = new MohStore(this.mohPath);
        }

        public void Dispose() => Directory.Delete(this.directory, recursive: true);

        private static MohFile Sample(long id = 1, string name = "Piano Loop") => new()
        {
            MohFileID = id,
            Name = name,
            File = MohFile.FileNameFor(id, name),
        };

        private string Write(MohFile file, int bytes = 16_044)
        {
            var path = Path.Combine(this.mohPath, file.File);
            File.WriteAllBytes(path, new byte[bytes]);
            return path;
        }

        [Fact]
        public void Every_track_lives_in_the_one_directory()
        {
            var file = Sample();

            Assert.Equal(Path.Combine(this.mohPath, file.File), this.store.PathFor(file.MohFileID, file.File));
        }

        [Theory]
        [InlineData("../../etc/asterisk/pjsip.conf")]
        [InlineData("/etc/passwd")]
        [InlineData("1-piano.wav/../../evil.wav")]
        [InlineData("piano-loop.wav")]
        public void A_file_name_we_did_not_derive_never_reaches_a_path(string fileName)
        {
            Assert.Throws<InvalidOperationException>(() => this.store.PathFor(1, fileName));
        }

        /// <summary>The name is derived from the ID, so there is no path to build before there is one.</summary>
        [Fact]
        public void A_track_that_has_not_been_saved_has_nowhere_to_put_its_audio()
        {
            Assert.Throws<InvalidOperationException>(() => this.store.PathFor(0, "1-piano-loop.wav"));
        }

        [Fact]
        public void A_track_with_a_file_on_disk_describes_it()
        {
            var file = Sample();
            this.Write(file);

            var audio = this.store.Describe(file);

            Assert.NotNull(audio);
            Assert.Equal(file.File, audio!.FileName);
            Assert.Equal(1.0, audio.Seconds, 1);
        }

        /// <summary>
        /// A row and a file can drift apart, and the UI says so rather than showing a length for
        /// nothing.
        /// </summary>
        [Fact]
        public void A_track_whose_file_is_missing_describes_nothing()
        {
            Assert.Null(this.store.Describe(Sample()));
            Assert.Null(this.store.Describe(new MohFile { MohFileID = 1, Name = "No audio" }));
        }

        [Fact]
        public void Renaming_moves_the_file_and_leaves_nothing_behind()
        {
            var file = Sample();
            this.Write(file);

            var renamed = MohFile.FileNameFor(file.MohFileID, "Guitar Loop");
            this.store.Rename(file.MohFileID, file.File, renamed);

            Assert.False(File.Exists(Path.Combine(this.mohPath, file.File)));
            Assert.True(File.Exists(Path.Combine(this.mohPath, renamed)));
        }

        [Fact]
        public void Deleting_a_track_removes_its_file_and_only_its_file()
        {
            var first = Sample();
            var second = Sample(2, "Guitar Loop");
            this.Write(first);
            this.Write(second);

            this.store.Delete(first);

            Assert.False(File.Exists(Path.Combine(this.mohPath, first.File)));
            Assert.True(File.Exists(Path.Combine(this.mohPath, second.File)));
        }

        [Fact]
        public void Deleting_a_track_with_no_file_is_not_an_error()
        {
            this.store.Delete(new MohFile { MohFileID = 1, Name = "No audio" });
            this.store.Delete(Sample());
        }

        /// <summary>
        /// The path is a constant rather than a setting because the generated musiconhold.conf
        /// names it: the two have to be the same directory or Asterisk plays nothing.
        /// </summary>
        [Fact]
        public void The_default_path_is_the_one_the_generated_class_names()
        {
            Assert.Equal("/var/lib/asterisk/moh", MohStore.DefaultMohPath);
            Assert.Equal(MohStore.DefaultMohPath, new MohStore().MohPath);
        }
    }
}
