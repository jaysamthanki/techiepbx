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
    }
}
