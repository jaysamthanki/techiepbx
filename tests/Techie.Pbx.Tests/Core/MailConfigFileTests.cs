using System.Text.Json;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Mail;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The file the voicemail mailcmd script reads (D126). It carries the SMTP password, so what
    /// matters here is as much what it does when the settings are unusable — take the file away
    /// rather than leave a stale credential — as what it writes when they are.
    /// </summary>
    public class MailConfigFileTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-mail-").FullName;

        public void Dispose() => Directory.Delete(this.directory, recursive: true);

        [Fact]
        public void A_complete_relay_is_written_as_the_script_reads_it()
        {
            var file = new MailConfigFile(this.directory);

            Assert.True(file.Write(Settings(
                (SettingsKeys.MailFromAddress, "pbx@example.com"),
                (SettingsKeys.MailFromName, "Acme PBX"),
                (SettingsKeys.MailSmtpHost, "smtp.example.com"),
                (SettingsKeys.MailSmtpPort, "2525"),
                (SettingsKeys.MailSmtpUsername, "apikey"),
                (SettingsKeys.MailSmtpPassword, "not-a-real-password"))));

            using var json = JsonDocument.Parse(File.ReadAllText(file.FilePath));
            var root = json.RootElement;

            Assert.Equal("pbx@example.com", root.GetProperty("From").GetString());
            Assert.Equal("Acme PBX", root.GetProperty("FromName").GetString());
            Assert.Equal("smtp.example.com", root.GetProperty("Host").GetString());
            Assert.Equal("not-a-real-password", root.GetProperty("Password").GetString());
            Assert.Equal(2525, root.GetProperty("Port").GetInt32());
            Assert.Equal("apikey", root.GetProperty("Username").GetString());
        }

        /// <summary>
        /// The relay a site does not authenticate to: an internal one that trusts this host by
        /// address. Still a usable file, because the script only needs somewhere to submit.
        /// </summary>
        [Fact]
        public void A_relay_without_a_username_is_still_written()
        {
            var file = new MailConfigFile(this.directory);

            Assert.True(file.Write(Settings(
                (SettingsKeys.MailFromAddress, "pbx@example.com"),
                (SettingsKeys.MailSmtpHost, "mail.internal"))));

            using var json = JsonDocument.Parse(File.ReadAllText(file.FilePath));

            Assert.Equal("", json.RootElement.GetProperty("Username").GetString());
            Assert.Equal(MailSettings.DefaultSmtpPort, json.RootElement.GetProperty("Port").GetInt32());
        }

        /// <summary>
        /// Clearing the relay has to stop the mail, not carry on with the credentials the file
        /// happens to be holding. Leaving it would also leave a password on disk for a feature
        /// the admin just switched off.
        /// </summary>
        [Fact]
        public void Clearing_the_relay_takes_the_file_away()
        {
            var file = new MailConfigFile(this.directory);

            file.Write(Settings(
                (SettingsKeys.MailFromAddress, "pbx@example.com"),
                (SettingsKeys.MailSmtpHost, "smtp.example.com")));

            Assert.True(File.Exists(file.FilePath));

            Assert.False(file.Write(Settings((SettingsKeys.MailFromAddress, "pbx@example.com"))));
            Assert.False(File.Exists(file.FilePath));
        }

        [Fact]
        public void Incomplete_settings_never_write_a_file_at_all()
        {
            var file = new MailConfigFile(this.directory);

            Assert.False(file.Write(Settings()));
            Assert.False(file.Write(Settings((SettingsKeys.MailSmtpHost, "smtp.example.com"))));
            Assert.False(file.Write(Settings((SettingsKeys.MailFromAddress, "pbx@example.com"))));
            Assert.False(File.Exists(file.FilePath));
        }

        [Fact]
        public void The_reason_it_cannot_be_written_is_said_plainly()
        {
            Assert.Equal("no SMTP host is set", MailConfigFile.Problem(Settings()));

            Assert.Equal("no from address is set", MailConfigFile.Problem(
                Settings((SettingsKeys.MailSmtpHost, "smtp.example.com"))));

            Assert.Null(MailConfigFile.Problem(Settings(
                (SettingsKeys.MailFromAddress, "pbx@example.com"),
                (SettingsKeys.MailSmtpHost, "smtp.example.com"))));
        }

        /// <summary>
        /// The asterisk user reads this file through the group; nobody else may read it at all,
        /// because the SMTP password is in it. Checked over an existing file too: a rename keeps
        /// the mode of whatever was renamed into place.
        /// </summary>
        [Fact]
        public void The_file_is_group_readable_and_never_world_readable()
        {
            if (OperatingSystem.IsWindows())
                return;

            var file = new MailConfigFile(this.directory);
            var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

            file.Write(Settings(
                (SettingsKeys.MailFromAddress, "pbx@example.com"),
                (SettingsKeys.MailSmtpHost, "smtp.example.com")));
            Assert.Equal(expected, File.GetUnixFileMode(file.FilePath));

            file.Write(Settings(
                (SettingsKeys.MailFromAddress, "pbx@example.com"),
                (SettingsKeys.MailSmtpHost, "other.example.com")));
            Assert.Equal(expected, File.GetUnixFileMode(file.FilePath));
        }

        [Fact]
        public void Writing_leaves_no_temp_file_behind()
        {
            var file = new MailConfigFile(this.directory);

            file.Write(Settings(
                (SettingsKeys.MailFromAddress, "pbx@example.com"),
                (SettingsKeys.MailSmtpHost, "smtp.example.com")));

            Assert.Single(Directory.GetFiles(this.directory));
            Assert.Equal(MailConfigFile.FileName, Path.GetFileName(file.FilePath));
        }

        /// <summary>The Config folder is made on the first write, so a fresh install needs nothing.</summary>
        [Fact]
        public void A_missing_directory_is_created()
        {
            var file = new MailConfigFile(Path.Combine(this.directory, MailConfigFile.DirectoryName));

            Assert.True(file.Write(Settings(
                (SettingsKeys.MailFromAddress, "pbx@example.com"),
                (SettingsKeys.MailSmtpHost, "smtp.example.com"))));

            Assert.True(File.Exists(file.FilePath));
        }

        private static MailSettings Settings(params (string Key, string Value)[] stored) =>
            new(stored.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
    }
}
