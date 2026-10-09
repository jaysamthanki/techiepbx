using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Techie.Pbx.Core.Migration;

namespace Techie.Pbx.Tests.Migration
{
    /// <summary>
    /// The tarball guard (D170): an export arrives through a browser from another machine, so one
    /// entry that would land outside the staging directory refuses the whole archive.
    /// </summary>
    public class MigrationArchiveTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-archive-").FullName;

        public void Dispose()
        {
            Directory.Delete(this.directory, recursive: true);
        }

        private static MemoryStream Archive(Action<TarWriter> write)
        {
            var output = new MemoryStream();

            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
                write(tar);

            output.Position = 0;
            return output;
        }

        private static void File(TarWriter tar, string name, string content)
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
            };
            tar.WriteEntry(entry);
        }

        private string Target()
        {
            var target = Path.Combine(this.directory, "stage");
            Directory.CreateDirectory(target);
            return target;
        }

        [Fact]
        public void A_real_shaped_export_unpacks()
        {
            using var archive = Archive(tar =>
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "files/"));
                File(tar, "manifest.json", "{\"manifestVersion\":1}");
                File(tar, "files/sounds/en/custom/Main.wav", "RIFF");
                File(tar, "./files/voicemail/101/INBOX/msg0000.txt", "[message]");
            });

            var target = this.Target();
            MigrationArchive.Extract(archive, target);

            Assert.True(System.IO.File.Exists(Path.Combine(target, "manifest.json")));
            Assert.True(System.IO.File.Exists(Path.Combine(target, "files", "sounds", "en", "custom", "Main.wav")));
            Assert.True(System.IO.File.Exists(Path.Combine(target, "files", "voicemail", "101", "INBOX", "msg0000.txt")));
        }

        [Theory]
        [InlineData("../evil.txt")]
        [InlineData("files/../../evil.txt")]
        [InlineData("files/sounds/../../../etc/cron.d/evil")]
        [InlineData("/etc/passwd")]
        [InlineData("/tmp/evil")]
        [InlineData("files\\..\\evil.txt")]
        public void Traversal_refuses_the_whole_archive(string name)
        {
            using var archive = Archive(tar =>
            {
                File(tar, "manifest.json", "{}");
                File(tar, name, "pwned");
            });

            var target = this.Target();

            Assert.Throws<MigrationArchiveException>(() => MigrationArchive.Extract(archive, target));
            Assert.False(System.IO.File.Exists(Path.Combine(this.directory, "evil.txt")));
        }

        [Theory]
        [InlineData(TarEntryType.SymbolicLink)]
        [InlineData(TarEntryType.HardLink)]
        public void Links_are_refused(TarEntryType type)
        {
            using var archive = Archive(tar =>
                tar.WriteEntry(new PaxTarEntry(type, "files/sounds/link") { LinkName = "/etc/shadow" }));

            var error = Assert.Throws<MigrationArchiveException>(() => MigrationArchive.Extract(archive, this.Target()));
            Assert.Contains("only files and folders", error.Message);
        }

        [Fact]
        public void More_than_the_cap_is_refused()
        {
            using var archive = Archive(tar =>
            {
                File(tar, "a.bin", new string('a', 600));
                File(tar, "b.bin", new string('b', 600));
            });

            var error = Assert.Throws<MigrationArchiveException>(() => MigrationArchive.Extract(archive, this.Target(), maxExtractedBytes: 1000));
            Assert.Contains("more than", error.Message);
        }

        [Fact]
        public void A_file_that_is_not_gzip_is_refused_in_words()
        {
            using var archive = new MemoryStream(Encoding.UTF8.GetBytes("this is not a tarball at all"));

            var error = Assert.Throws<MigrationArchiveException>(() => MigrationArchive.Extract(archive, this.Target()));
            Assert.Contains("gzip", error.Message);
        }

        [Theory]
        [InlineData("manifest.json", true)]
        [InlineData("files/voicemail/101/INBOX/msg0000.wav", true)]
        [InlineData("./manifest.json", true)]
        [InlineData("files/..hidden", true)]
        [InlineData("..", false)]
        [InlineData("a/../b", false)]
        [InlineData("/abs", false)]
        [InlineData("C:/windows", false)]
        [InlineData("a\\b", false)]
        [InlineData("a\0b", false)]
        [InlineData("", false)]
        public void Names(string name, bool allowed)
        {
            Assert.Equal(allowed, MigrationArchive.NameProblem(name) == null);
        }
    }
}
