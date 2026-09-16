using Microsoft.Data.Sqlite;
using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Covers the database to files half of "apply config" and the decision about which modules
    /// need a reload. The reload itself needs a real Asterisk, so it is checked on the lab VM.
    /// </summary>
    public class ConfigApplierTests : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("tnpbx-apply-").FullName;
        private readonly string _confDirectory;
        private readonly ExtensionRepository _extensions;
        private readonly SettingsRepository _settings;
        private readonly ConfigApplier _applier;

        public ConfigApplierTests()
        {
            _confDirectory = Path.Combine(_directory, "asterisk");
            Directory.CreateDirectory(_confDirectory);

            var database = new Database(Path.Combine(_directory, "tnpbx.db"));
            database.Migrate();
            _extensions = new ExtensionRepository(database);
            _settings = new SettingsRepository(database);

            // Port 1 has nothing listening: any attempt to reload would fail loudly.
            var ami = new AmiSettings { Port = 1, Username = "tnpbx", Secret = "not-a-real-secret", TimeoutSeconds = 1 };
            _applier = new ConfigApplier(_confDirectory, new PjsipTransport(), _extensions, ami);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, recursive: true);
        }

        private void AddExtension(string number, string name, string secret) =>
            _extensions.Insert(new Extension { Number = number, Name = name, Secret = secret });

        [Fact]
        public void Renders_every_file_with_the_module_that_owns_it()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");

            var files = _applier.Render();

            Assert.Equal(new[] { "pjsip.conf", "extensions.conf" }, files.Select(f => f.FileName));
            Assert.Equal(ConfigApplier.PjsipModule, files[0].Module);
            Assert.Equal(ConfigApplier.DialplanModule, files[1].Module);
            Assert.Contains("[1001]", files[0].Content);
            Assert.Contains("exten => 1001,1,Dial(PJSIP/1001,30)", files[1].Content);
        }

        [Fact]
        public void First_write_writes_every_file_and_a_repeat_writes_none()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");

            Assert.Equal(new[] { "pjsip.conf", "extensions.conf" }, _applier.Write().Select(f => f.FileName));
            Assert.True(File.Exists(Path.Combine(_confDirectory, "pjsip.conf")));
            Assert.True(File.Exists(Path.Combine(_confDirectory, "extensions.conf")));

            Assert.Empty(_applier.Write());
        }

        [Fact]
        public void A_secret_change_only_touches_pjsip_conf()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");
            _applier.Write();

            var extension = _extensions.GetByNumber("1001")!;
            extension.Secret = "EEEEffffGGGGhhhh2222";
            _extensions.Update(extension);

            var changed = _applier.Write();

            Assert.Single(changed);
            Assert.Equal("pjsip.conf", changed[0].FileName);
            Assert.Equal(ConfigApplier.PjsipModule, changed[0].Module);
        }

        [Fact]
        public void A_new_extension_touches_both_files()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");
            _applier.Write();

            AddExtension("1002", "Sales", "EEEEffffGGGGhhhh2222");

            Assert.Equal(
                new[] { ConfigApplier.PjsipModule, ConfigApplier.DialplanModule },
                _applier.Write().Select(f => f.Module));
        }

        [Fact]
        public void Apply_does_not_open_an_ami_connection_when_nothing_changed()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");
            _applier.Write();

            var result = _applier.Apply();

            Assert.Empty(result.ChangedFiles);
            Assert.Empty(result.ReloadedModules);
        }

        [Fact]
        public void Apply_reports_that_asterisk_is_unreachable_rather_than_failing_silently()
        {
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");

            var ex = Assert.Throws<AmiException>(() => _applier.Apply());

            Assert.Contains("Could not connect to AMI", ex.Message);

            // The files were still written: the database stays the source of truth and a later
            // apply only has to reload.
            Assert.True(File.Exists(Path.Combine(_confDirectory, "pjsip.conf")));
        }

        [Fact]
        public void FromDatabase_takes_the_conf_directory_transport_and_ami_details_from_settings()
        {
            _settings.Set(SettingsKeys.AsteriskConfDirectory, _confDirectory);
            _settings.Set(SettingsKeys.SipExternalAddress, "203.0.113.10");
            _settings.Set(SettingsKeys.SipLocalNets, "10.8.20.0/24");
            _settings.Set(SettingsKeys.AmiUsername, "tnpbx");
            _settings.Set(SettingsKeys.AmiSecret, "not-a-real-secret");
            AddExtension("1001", "Front Desk", "AAAAbbbbCCCCdddd1111");

            var applier = ConfigApplier.FromDatabase(_settings, _extensions);
            var changed = applier.Write();

            Assert.Equal(new[] { "pjsip.conf", "extensions.conf" }, changed.Select(f => f.FileName));
            var pjsip = File.ReadAllText(Path.Combine(_confDirectory, "pjsip.conf"));
            Assert.Contains("external_media_address = 203.0.113.10", pjsip);
            Assert.Contains("local_net = 10.8.20.0/24", pjsip);
        }

        [Fact]
        public void An_empty_database_still_renders_a_usable_dialplan()
        {
            var files = _applier.Render();

            Assert.Contains("[internal]", files[1].Content);
            Assert.Contains("exten => *43,1,Answer()", files[1].Content);
            Assert.Contains("[transport-udp]", files[0].Content);
        }
    }
}
