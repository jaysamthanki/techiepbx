using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// What may be typed into an extension's forwarding field (D130). The shape is the model's
    /// business; whether a short number is really an extension needs the other rows, so that half
    /// is checked through the repository.
    /// </summary>
    public class ExtensionForwardingTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-db-").FullName;
        private readonly ExtensionRepository repository;

        public ExtensionForwardingTests()
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

        private static Extension Forwarding(string forwarding) => new()
        {
            Number = "1001",
            Name = "Front Desk",
            Secret = "AAAAbbbbCCCCdddd1111",
            Forwarding = forwarding,
        };

        /// <summary>The whole field's errors, so a test can say what it does not care about.</summary>
        private static List<string> Errors(string forwarding) => Forwarding(forwarding).Validate();

        [Fact]
        public void An_empty_field_is_fine_and_means_normal_ringing()
        {
            Assert.Empty(Errors(""));
            Assert.Empty(Errors("   "));
            Assert.Empty(Forwarding("").ForwardingList());
        }

        [Fact]
        public void An_extension_number_is_a_target()
        {
            Assert.Empty(Errors("103"));
        }

        [Fact]
        public void A_full_phone_number_is_a_target()
        {
            Assert.Empty(Errors("7146085242"));
        }

        [Fact]
        public void Both_at_once_is_the_point_of_the_field()
        {
            Assert.Empty(Errors("103 7146085242"));
            Assert.Equal(new[] { "103", "7146085242" }, Forwarding("103 7146085242").ForwardingList());
        }

        /// <summary>Any run of whitespace separates, so a stray double space is not an empty target.</summary>
        [Fact]
        public void Runs_of_whitespace_separate_targets()
        {
            Assert.Empty(Errors("103   7146085242"));
            Assert.Equal(new[] { "103", "7146085242" }, Forwarding(" 103\t7146085242 ").ForwardingList());
        }

        /// <summary>
        /// The toll-fraud rule outbound routes make, arriving from the other side (D47): 00 and 011
        /// are international dialling, so a number starting with 0 is refused rather than dialled.
        /// </summary>
        [Fact]
        public void A_number_starting_with_zero_is_refused()
        {
            var errors = Errors("011441234567890");

            Assert.Single(errors);
            Assert.Contains("may not start with 0", errors[0]);
        }

        [Fact]
        public void A_target_that_is_not_digits_is_refused_and_named()
        {
            var errors = Errors("mobile");

            Assert.Single(errors);
            Assert.Contains("'mobile'", errors[0]);
        }

        /// <summary>
        /// The field is space separated and nothing else, so a comma makes one impossible target
        /// rather than two good ones. Refusing it is how the user finds that out.
        /// </summary>
        [Fact]
        public void Commas_are_not_separators()
        {
            var errors = Errors("103,7146085242");

            Assert.Single(errors);
            Assert.Contains("'103,7146085242'", errors[0]);
        }

        [Fact]
        public void More_than_four_targets_is_refused()
        {
            var errors = Errors("101 102 103 104 105");

            Assert.Single(errors);
            Assert.Contains("at most 4", errors[0]);
        }

        [Fact]
        public void The_same_target_twice_is_refused()
        {
            var errors = Errors("103 103");

            Assert.Single(errors);
            Assert.Contains("only ring the same place once", errors[0]);
        }

        [Fact]
        public void A_field_longer_than_the_cap_is_refused_without_quoting_it_back()
        {
            var errors = Errors(new string('7', Extension.MaxForwardingLength + 1));

            Assert.Single(errors);
            Assert.Contains("64 characters or fewer", errors[0]);
        }

        /// <summary>
        /// A short number is an extension — the rule dialling from a phone already follows — so a
        /// typo is a mistake to report rather than a number to hand to a provider.
        /// </summary>
        [Fact]
        public void An_extension_that_does_not_exist_is_refused()
        {
            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.repository.Insert(Forwarding("104")));

            Assert.Contains("There is no extension 104", ex.Message);
        }

        [Fact]
        public void An_extension_that_is_switched_off_is_refused()
        {
            this.repository.Insert(new Extension
            {
                Number = "104",
                Name = "Spare",
                Secret = SecretGenerator.Create(),
                Enabled = false,
            });

            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.repository.Insert(Forwarding("104")));

            Assert.Contains("is disabled", ex.Message);
        }

        [Fact]
        public void An_extension_that_exists_is_saved_and_read_back()
        {
            this.repository.Insert(new Extension { Number = "104", Name = "Spare", Secret = SecretGenerator.Create() });
            this.repository.Insert(Forwarding("104 7146085242"));

            Assert.Equal("104 7146085242", this.repository.GetByNumber("1001")?.Forwarding);
        }

        /// <summary>
        /// Its own number in its own list is not a loop and is not a mistake: it is how somebody
        /// keeps their desk phone ringing while adding a mobile to it.
        /// </summary>
        [Fact]
        public void An_extension_may_forward_to_itself_as_well_as_elsewhere()
        {
            this.repository.Insert(Forwarding("1001 7146085242"));

            Assert.Equal("1001 7146085242", this.repository.GetByNumber("1001")?.Forwarding);
        }

        /// <summary>A number long enough to be an outside number is never looked up as an extension.</summary>
        [Fact]
        public void A_full_phone_number_needs_no_extension_behind_it()
        {
            this.repository.Insert(Forwarding("7146085242"));

            Assert.Equal("7146085242", this.repository.GetByNumber("1001")?.Forwarding);
        }

        /// <summary>Forwarding is left alone by an edit that does not mention it.</summary>
        [Fact]
        public void An_update_keeps_the_forwarding_it_was_given()
        {
            var id = this.repository.Insert(Forwarding("7146085242"));

            var extension = this.repository.GetByID(id)!;
            extension.Name = "Reception";
            this.repository.Update(extension);

            Assert.Equal("7146085242", this.repository.GetByNumber("1001")?.Forwarding);
        }
    }
}
