using Techie.Pbx.Asterisk.Config;

namespace Techie.Pbx.Tests.Asterisk
{
    public class ConfFileWriterTests : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("tnpbx-conf-").FullName;

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        [Fact]
        public void Writes_content_and_leaves_no_temp_files()
        {
            Assert.True(ConfFileWriter.WriteAtomic(_directory, "pjsip.conf", "hello\n"));

            Assert.Equal("hello\n", File.ReadAllText(Path.Combine(_directory, "pjsip.conf")));
            Assert.Single(Directory.GetFiles(_directory));
        }

        [Fact]
        public void Returns_false_when_content_is_unchanged()
        {
            ConfFileWriter.WriteAtomic(_directory, "pjsip.conf", "hello\n");
            Assert.False(ConfFileWriter.WriteAtomic(_directory, "pjsip.conf", "hello\n"));
            Assert.True(ConfFileWriter.WriteAtomic(_directory, "pjsip.conf", "changed\n"));
        }

        /// <summary>
        /// Asterisk reads the generated files as a member of the file's group, so group read has
        /// to be there; the files hold SIP secrets, so "other" must not be (D18).
        /// </summary>
        [Fact]
        public void Written_files_are_group_readable_and_not_world_readable()
        {
            if (OperatingSystem.IsWindows())
                return;

            var target = Path.Combine(_directory, "pjsip.conf");

            // Once onto a new file and once over an existing one, which is the case that matters:
            // a rename keeps the mode of whatever was renamed into place.
            ConfFileWriter.WriteAtomic(_directory, "pjsip.conf", "hello\n");
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
                File.GetUnixFileMode(target));

            ConfFileWriter.WriteAtomic(_directory, "pjsip.conf", "changed\n");
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
                File.GetUnixFileMode(target));
        }
    }
}
