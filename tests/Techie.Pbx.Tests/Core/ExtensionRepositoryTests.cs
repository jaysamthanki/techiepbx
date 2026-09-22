using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Tests.Core
{
    public class ExtensionRepositoryTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-db-").FullName;
        private readonly ExtensionRepository repository;

        public ExtensionRepositoryTests()
        {
            var database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            database.Migrate();
            this.repository = new ExtensionRepository(database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        [Fact]
        public void Insert_then_read_back()
        {
            var secret = SecretGenerator.Create();
            var id = this.repository.Insert(new Extension { Number = "1001", Name = "Front Desk", Secret = secret });

            var loaded = this.repository.GetByNumber("1001");

            Assert.NotNull(loaded);
            Assert.Equal(id, loaded.ExtensionID);
            Assert.Equal("Front Desk", loaded.Name);
            Assert.Equal(secret, loaded.Secret);
            Assert.True(loaded.Enabled);
        }

        [Fact]
        public void GetAll_orders_numerically()
        {
            this.repository.Insert(new Extension { Number = "200", Name = "B", Secret = SecretGenerator.Create() });
            this.repository.Insert(new Extension { Number = "1000", Name = "C", Secret = SecretGenerator.Create() });
            this.repository.Insert(new Extension { Number = "30", Name = "A", Secret = SecretGenerator.Create() });

            Assert.Equal(new[] { "30", "200", "1000" }, this.repository.GetAll().Select(e => e.Number));
        }

        /// <summary>The UI works by ExtensionID, because the number is editable.</summary>
        [Fact]
        public void GetByID_reads_one_extension_and_answers_null_for_one_that_is_gone()
        {
            var id = this.repository.Insert(new Extension { Number = "1001", Name = "Front Desk", Secret = SecretGenerator.Create() });

            var loaded = this.repository.GetByID(id);

            Assert.NotNull(loaded);
            Assert.Equal("1001", loaded.Number);
            Assert.Null(this.repository.GetByID(id + 1));
        }

        [Fact]
        public void Duplicate_number_is_a_validation_error()
        {
            this.repository.Insert(new Extension { Number = "1001", Name = "One", Secret = SecretGenerator.Create() });

            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.repository.Insert(new Extension { Number = "1001", Name = "Two", Secret = SecretGenerator.Create() }));
            Assert.Contains("already exists", ex.Message);
        }

        [Fact]
        public void Update_and_delete()
        {
            var extension = new Extension { Number = "1001", Name = "Old", Secret = SecretGenerator.Create() };
            this.repository.Insert(extension);

            extension.Name = "New";
            extension.Enabled = false;
            this.repository.Update(extension);

            var loaded = this.repository.GetByNumber("1001")!;
            Assert.Equal("New", loaded.Name);
            Assert.False(loaded.Enabled);

            this.repository.Delete(extension.ExtensionID);
            Assert.Null(this.repository.GetByNumber("1001"));
        }

        [Fact]
        public void Voicemail_settings_survive_a_round_trip()
        {
            var extension = new Extension
            {
                Number = "1001",
                Name = "Front Desk",
                Secret = SecretGenerator.Create(),
                VoicemailEnabled = true,
                VoicemailPin = "4321",
                VoicemailEmail = "desk@example.com",
                VoicemailAttachRecording = false,
                VoicemailDeleteAfterEmail = true,
                VoicemailTranscribe = true,
            };
            this.repository.Insert(extension);

            var loaded = this.repository.GetByNumber("1001")!;

            Assert.True(loaded.VoicemailEnabled);
            Assert.Equal("4321", loaded.VoicemailPin);
            Assert.Equal("desk@example.com", loaded.VoicemailEmail);
            Assert.False(loaded.VoicemailAttachRecording);
            Assert.True(loaded.VoicemailDeleteAfterEmail);
            Assert.True(loaded.VoicemailTranscribe);

            // And an update carries it back off again: a column that only ever goes one way is a
            // column the UPDATE forgot.
            loaded.VoicemailTranscribe = false;
            this.repository.Update(loaded);

            Assert.False(this.repository.GetByNumber("1001")!.VoicemailTranscribe);
        }

        /// <summary>An extension that never asked for voicemail has none, and needs no PIN.</summary>
        [Fact]
        public void An_extension_without_voicemail_needs_no_pin()
        {
            this.repository.Insert(new Extension { Number = "1001", Name = "Front Desk", Secret = SecretGenerator.Create() });

            var loaded = this.repository.GetByNumber("1001")!;

            Assert.False(loaded.VoicemailEnabled);
            Assert.Equal("", loaded.VoicemailPin);
            Assert.True(loaded.VoicemailAttachRecording);
            Assert.False(loaded.VoicemailDeleteAfterEmail);
            Assert.False(loaded.VoicemailTranscribe);
        }

        [Theory]
        [InlineData("")]
        [InlineData("123")]
        [InlineData("123456789")]
        [InlineData("12a4")]
        public void Voicemail_needs_a_pin_of_four_to_eight_digits_once_it_is_switched_on(string pin)
        {
            var extension = new Extension
            {
                Number = "1001",
                Name = "Front Desk",
                Secret = SecretGenerator.Create(),
                VoicemailEnabled = true,
                VoicemailPin = pin,
            };

            var ex = Assert.Throws<ValidationFailedException>(() => this.repository.Insert(extension));

            Assert.Contains("PIN", ex.Message);
        }

        /// <summary>
        /// The extension's own outbound caller ID (D125), in both the forms an admin might type it,
        /// and empty for the extension that has none — which is the default and the common case.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("17141234567")]
        [InlineData("\"Jane Smith\" <17141234567>")]
        public void An_outbound_caller_id_survives_a_round_trip(string callerID)
        {
            var extension = new Extension
            {
                Number = "1001",
                Name = "Front Desk",
                Secret = SecretGenerator.Create(),
                OutboundCallerID = callerID,
            };
            this.repository.Insert(extension);

            Assert.Equal(callerID, this.repository.GetByNumber("1001")!.OutboundCallerID);

            extension.OutboundCallerID = "";
            this.repository.Update(extension);

            Assert.Equal("", this.repository.GetByNumber("1001")!.OutboundCallerID);
        }

        /// <summary>
        /// What is refused is what could not be written into a conf file, or could be written into one
        /// and mean something else: a number with anything but digits in it, a name with a bracket,
        /// comma or quote of its own, and a name with no number behind it (D125).
        /// </summary>
        [Theory]
        [InlineData("+17141234567")]
        [InlineData("1 714 123 4567")]
        [InlineData("Jane Smith")]
        [InlineData("\"Jane, Smith\" <17141234567>")]
        [InlineData("\"Jane (Sales)\" <17141234567>")]
        [InlineData("\"Jane\" <714123456789012345>")]
        public void An_outbound_caller_id_that_could_not_be_written_is_refused(string callerID)
        {
            var extension = new Extension
            {
                Number = "1001",
                Name = "Front Desk",
                Secret = SecretGenerator.Create(),
                OutboundCallerID = callerID,
            };

            var ex = Assert.Throws<ValidationFailedException>(() => this.repository.Insert(extension));

            Assert.Contains("Outbound caller ID", ex.Message);
        }

        [Fact]
        public void A_voicemail_email_that_is_not_an_address_is_rejected()
        {
            var extension = new Extension
            {
                Number = "1001",
                Name = "Front Desk",
                Secret = SecretGenerator.Create(),
                VoicemailEmail = "not-an-address",
            };

            var ex = Assert.Throws<ValidationFailedException>(() => this.repository.Insert(extension));

            Assert.Contains("email", ex.Message);
        }

        [Fact]
        public void Invalid_extension_is_rejected_before_hitting_the_database()
        {
            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.repository.Insert(new Extension { Number = "1x", Name = "Bad\nName", Secret = "short" }));

            Assert.Equal(3, ex.Errors.Count);
            Assert.Empty(this.repository.GetAll());
        }

        [Fact]
        public void Migrate_is_idempotent()
        {
            var database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            database.Migrate();
            database.Migrate();
        }
    }
}
