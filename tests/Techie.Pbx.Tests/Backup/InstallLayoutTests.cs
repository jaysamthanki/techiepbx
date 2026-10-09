using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Backup;

namespace Techie.Pbx.Tests.Backup
{
    /// <summary>
    /// The CLI finds the database and the sounds the way the web app does: from the same
    /// appsettings.json keys, relative paths under the install folder.
    /// </summary>
    public class InstallLayoutTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-layout-").FullName;

        public void Dispose()
        {
            Directory.Delete(this.directory, recursive: true);
        }

        private InstallLayout Read(string json)
        {
            var path = Path.Combine(this.directory, "appsettings.json");
            File.WriteAllText(path, json);
            return InstallLayout.Read(path);
        }

        [Fact]
        public void The_shipped_settings_resolve_to_the_installed_paths()
        {
            var layout = this.Read("""
                {
                  // comments are allowed, as ASP.NET allows them
                  "Database": { "Path": "Data/tnpbx.db" },
                  "Announcements": { "SoundsPath": "/var/lib/asterisk/sounds/tnpbx" },
                }
                """);

            Assert.Equal(Path.Combine(this.directory, "Data", "tnpbx.db"), layout.DatabasePath);
            Assert.Equal("/var/lib/asterisk/sounds/tnpbx", layout.SoundsPath);
            Assert.Equal(VoicemailSpool.Root, layout.VoicemailPath);
            Assert.Equal(Path.Combine(this.directory, "backups"), layout.BackupsDirectory);
            Assert.Equal(InstallLayout.DefaultStagingRoot, layout.StagingRoot);
        }

        [Fact]
        public void Missing_settings_fall_back_to_the_web_apps_defaults()
        {
            var layout = this.Read("{}");

            Assert.Equal(Path.Combine(this.directory, "Data", "tnpbx.db"), layout.DatabasePath);
            Assert.Equal(AnnouncementStore.DefaultSoundsPath, layout.SoundsPath);
        }

        [Fact]
        public void Keys_match_without_regard_to_case_like_ASP_NET_configuration()
        {
            var layout = this.Read("""{ "database": { "path": "/srv/pbx.db" }, "ANNOUNCEMENTS": { "soundspath": "sounds" } }""");

            Assert.Equal("/srv/pbx.db", layout.DatabasePath);
            Assert.Equal(Path.Combine(this.directory, "sounds"), layout.SoundsPath);
        }

        [Fact]
        public void An_unreadable_settings_file_is_a_clear_error()
        {
            Assert.Throws<BackupException>(() => this.Read("{ not json"));
            Assert.Throws<BackupException>(() => InstallLayout.Read(Path.Combine(this.directory, "missing.json")));
        }
    }
}
