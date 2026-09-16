using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Tests.Core
{
    public class SettingsRepositoryTests : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("tnpbx-settings-").FullName;
        private readonly SettingsRepository _repository;

        public SettingsRepositoryTests()
        {
            var database = new Database(Path.Combine(_directory, "tnpbx.db"));
            database.Migrate();
            _repository = new SettingsRepository(database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, recursive: true);
        }

        [Fact]
        public void Set_then_read_back()
        {
            _repository.Set(SettingsKeys.AmiUsername, "tnpbx");

            Assert.Equal("tnpbx", _repository.Get(SettingsKeys.AmiUsername));
        }

        [Fact]
        public void Setting_the_same_key_twice_replaces_the_value()
        {
            _repository.Set(SettingsKeys.AmiPort, "5038");
            _repository.Set(SettingsKeys.AmiPort, "5039");

            Assert.Equal("5039", _repository.Get(SettingsKeys.AmiPort));
            Assert.Single(_repository.GetAll());
        }

        [Fact]
        public void The_ami_secret_lives_in_the_database_like_any_other_setting()
        {
            _repository.Set(SettingsKeys.AmiSecret, "not-a-real-secret");

            Assert.Equal("not-a-real-secret", _repository.GetAll()[SettingsKeys.AmiSecret]);
            Assert.True(SettingsKeys.IsSecret(SettingsKeys.AmiSecret));
            Assert.False(SettingsKeys.IsSecret(SettingsKeys.AmiUsername));
        }

        [Fact]
        public void An_unknown_key_is_a_validation_error_rather_than_a_silent_write()
        {
            var ex = Assert.Throws<ValidationFailedException>(() => _repository.Set("Ami.Scret", "5038"));

            Assert.Contains("not a known setting", ex.Message);
            Assert.Empty(_repository.GetAll());
        }

        [Fact]
        public void An_over_long_value_is_rejected()
        {
            Assert.Throws<ValidationFailedException>(() =>
                _repository.Set(SettingsKeys.SipLocalNets, new string('x', 1025)));
        }

        [Fact]
        public void Get_returns_null_for_a_setting_that_was_never_written()
        {
            Assert.Null(_repository.Get(SettingsKeys.SipExternalAddress));
            Assert.Empty(_repository.GetAll());
        }

        [Fact]
        public void Delete_puts_a_setting_back_to_its_default()
        {
            _repository.Set(SettingsKeys.SipExternalAddress, "203.0.113.10");
            _repository.Delete(SettingsKeys.SipExternalAddress);

            Assert.Null(_repository.Get(SettingsKeys.SipExternalAddress));
        }

        [Fact]
        public void GetAll_returns_every_setting_in_one_map()
        {
            _repository.Set(SettingsKeys.AmiHost, "127.0.0.1");
            _repository.Set(SettingsKeys.AmiUsername, "tnpbx");

            var settings = _repository.GetAll();

            Assert.Equal(2, settings.Count);
            Assert.Equal("127.0.0.1", settings[SettingsKeys.AmiHost]);
            Assert.Equal("tnpbx", settings[SettingsKeys.AmiUsername]);
        }
    }
}
