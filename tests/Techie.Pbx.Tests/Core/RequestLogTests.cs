using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Diagnostics;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Where the W3C request log lives, and whether it is being written (D116). Two things are
    /// worth pinning down: the directory is inside the install, because that is the only place the
    /// hardened systemd unit can write (ReadWritePaths=/opt/tnpbx, never /var/log); and the Logs
    /// page finds the file the logger is writing to right now without ever being told a path.
    /// </summary>
    public class RequestLogTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-requestlog-").FullName;

        public void Dispose() => Directory.Delete(this.directory, recursive: true);

        [Fact]
        public void The_log_directory_is_inside_the_install()
        {
            var resolved = RequestLog.DirectoryIn("/opt/tnpbx");

            Assert.Equal("/opt/tnpbx/logs/requests", resolved);
            Assert.StartsWith("/opt/tnpbx/", resolved);
        }

        /// <summary>
        /// The one thing this must never be: a path under /var/log, which the unit's
        /// ProtectSystem=strict makes read-only and which is not this application's to begin with.
        /// </summary>
        [Fact]
        public void The_log_directory_is_never_under_var_log()
        {
            Assert.DoesNotContain("/var/log", RequestLog.DirectoryIn("/opt/tnpbx"));
        }

        [Fact]
        public void Logging_is_on_when_nobody_has_said_otherwise()
        {
            Assert.True(RequestLog.IsEnabled(new Dictionary<string, string>()));
        }

        [Theory]
        [InlineData("on", true)]
        [InlineData("off", false)]
        [InlineData("", true)]
        public void The_setting_decides_whether_it_is_on(string stored, bool expected)
        {
            var settings = new Dictionary<string, string> { [SettingsKeys.WebRequestLog] = stored };

            Assert.Equal(expected, RequestLog.IsEnabled(settings));
        }

        [Fact]
        public void A_missing_directory_is_no_file_rather_than_a_throw()
        {
            Assert.Null(RequestLog.Newest(Path.Combine(this.directory, "not-created-yet")));
        }

        [Fact]
        public void An_empty_directory_is_no_file()
        {
            Assert.Null(RequestLog.Newest(this.directory));
        }

        /// <summary>
        /// The logger starts a new file each day and whenever the current one fills up, so the one
        /// the page should tail is the one most recently written to.
        /// </summary>
        [Fact]
        public void The_newest_file_with_our_prefix_is_the_one_to_read()
        {
            this.WriteFile(RequestLog.FileNamePrefix + "20260918.0000.txt", DateTime.UtcNow.AddDays(-1));
            var current = this.WriteFile(RequestLog.FileNamePrefix + "20260919.0000.txt", DateTime.UtcNow);

            Assert.Equal(current, RequestLog.Newest(this.directory));
        }

        /// <summary>
        /// Somebody else's file in the same folder is not the request log, however new it is.
        /// </summary>
        [Fact]
        public void A_file_without_our_prefix_is_not_the_request_log()
        {
            var ours = this.WriteFile(RequestLog.FileNamePrefix + "20260919.0000.txt", DateTime.UtcNow.AddHours(-1));
            this.WriteFile("something-else.txt", DateTime.UtcNow);

            Assert.Equal(ours, RequestLog.Newest(this.directory));
        }

        private string WriteFile(string name, DateTime lastWriteUtc)
        {
            var path = Path.Combine(this.directory, name);

            File.WriteAllText(path, "#Version: 1.0\n");
            File.SetLastWriteTimeUtc(path, lastWriteUtc);

            return path;
        }
    }
}
