using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Tests.Core
{
    public class SettingsRepositoryTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-settings-").FullName;
        private readonly ConfigPendingMarker pending;
        private readonly SettingsRepository repository;

        public SettingsRepositoryTests()
        {
            var database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            database.Migrate();

            this.pending = new ConfigPendingMarker(database);
            this.repository = new SettingsRepository(database);
            this.pending.Clear();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        [Fact]
        public void Set_then_read_back()
        {
            this.repository.Set(SettingsKeys.AmiUsername, "tnpbx");

            Assert.Equal("tnpbx", this.repository.Get(SettingsKeys.AmiUsername));
        }

        [Fact]
        public void Setting_the_same_key_twice_replaces_the_value()
        {
            this.repository.Set(SettingsKeys.AmiPort, "5038");
            this.repository.Set(SettingsKeys.AmiPort, "5039");

            Assert.Equal("5039", this.repository.Get(SettingsKeys.AmiPort));
            Assert.Single(this.repository.GetAll());
        }

        [Fact]
        public void The_ami_secret_lives_in_the_database_like_any_other_setting()
        {
            this.repository.Set(SettingsKeys.AmiSecret, "not-a-real-secret");

            Assert.Equal("not-a-real-secret", this.repository.GetAll()[SettingsKeys.AmiSecret]);
            Assert.True(SettingsKeys.IsSecret(SettingsKeys.AmiSecret));
            Assert.False(SettingsKeys.IsSecret(SettingsKeys.AmiUsername));
        }

        [Fact]
        public void An_unknown_key_is_a_validation_error_rather_than_a_silent_write()
        {
            var ex = Assert.Throws<ValidationFailedException>(() => this.repository.Set("Ami.Scret", "5038"));

            Assert.Contains("not a known setting", ex.Message);
            Assert.Empty(this.repository.GetAll());
        }

        [Fact]
        public void An_over_long_value_is_rejected()
        {
            Assert.Throws<ValidationFailedException>(() =>
                this.repository.Set(SettingsKeys.SipLocalNets, new string('x', 1025)));
        }

        [Fact]
        public void Get_returns_null_for_a_setting_that_was_never_written()
        {
            Assert.Null(this.repository.Get(SettingsKeys.SipExternalAddress));
            Assert.Empty(this.repository.GetAll());
        }

        [Fact]
        public void Delete_puts_a_setting_back_to_its_default()
        {
            this.repository.Set(SettingsKeys.SipExternalAddress, "203.0.113.10");
            this.repository.Delete(SettingsKeys.SipExternalAddress);

            Assert.Null(this.repository.Get(SettingsKeys.SipExternalAddress));
        }

        /// <summary>
        /// Every one of these keys is read while config is generated, so storing one is a config
        /// change and the apply banner has to say so, exactly as editing an extension does (D67).
        /// </summary>
        [Fact]
        public void Setting_a_value_raises_the_apply_is_due_marker()
        {
            Assert.False(this.pending.IsPending);

            this.repository.Set(SettingsKeys.SipCodecs, "ulaw");

            Assert.True(this.pending.IsPending);
        }

        [Fact]
        public void Clearing_a_setting_raises_the_marker_too()
        {
            this.repository.Set(SettingsKeys.SipCodecs, "ulaw");
            this.pending.Clear();

            this.repository.Delete(SettingsKeys.SipCodecs);

            Assert.True(this.pending.IsPending);
        }

        /// <summary>
        /// The repository refuses a value the key cannot hold, not just an unknown key: the page
        /// and the database ask <see cref="SettingsValidation"/> the same question (D67).
        /// </summary>
        [Theory]
        [InlineData(SettingsKeys.SipTcpPort, "not a port")]
        [InlineData(SettingsKeys.SipStunServer, "stun server:70000")]
        [InlineData(SettingsKeys.SipCodecs, "opus")]
        public void A_value_the_key_cannot_hold_is_refused(string key, string value)
        {
            Assert.Throws<ValidationFailedException>(() => this.repository.Set(key, value));
            Assert.Null(this.repository.Get(key));
        }

        [Fact]
        public void GetAll_returns_every_setting_in_one_map()
        {
            this.repository.Set(SettingsKeys.AmiHost, "127.0.0.1");
            this.repository.Set(SettingsKeys.AmiUsername, "tnpbx");

            var settings = this.repository.GetAll();

            Assert.Equal(2, settings.Count);
            Assert.Equal("127.0.0.1", settings[SettingsKeys.AmiHost]);
            Assert.Equal("tnpbx", settings[SettingsKeys.AmiUsername]);
        }
    }
}
