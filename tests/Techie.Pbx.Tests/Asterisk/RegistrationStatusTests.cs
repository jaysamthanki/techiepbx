using Techie.Pbx.Asterisk.Ami;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The mapping from PJSIP contacts to the badge the extensions page shows. Reading contacts
    /// over a real connection is checked on the lab VM; this is the part that decides what the
    /// admin sees.
    /// </summary>
    public class RegistrationStatusTests
    {
        private static PjsipContact Contact(string endpoint, string status) =>
            new() { Aor = endpoint, Status = status };

        [Fact]
        public void An_extension_with_no_contact_is_not_registered()
        {
            var states = RegistrationStatus.Map(new List<PjsipContact>(), new[] { "1001", "1002" });

            Assert.Equal(RegistrationState.NotRegistered, states["1001"]);
            Assert.Equal(RegistrationState.NotRegistered, states["1002"]);
        }

        /// <summary>
        /// A contact means a phone registered. Only "Unreachable" says qualify stopped getting
        /// answers; "NonQualified" is qualify switched off, which is our default.
        /// </summary>
        [Theory]
        [InlineData("Reachable")]
        [InlineData("NonQualified")]
        [InlineData("Unknown")]
        [InlineData("Created")]
        public void A_contact_that_is_not_unreachable_counts_as_registered(string status)
        {
            var states = RegistrationStatus.Map(new[] { Contact("1001", status) }, new[] { "1001" });

            Assert.Equal(RegistrationState.Registered, states["1001"]);
        }

        [Fact]
        public void An_unreachable_contact_says_so()
        {
            var states = RegistrationStatus.Map(new[] { Contact("1001", "Unreachable") }, new[] { "1001" });

            Assert.Equal(RegistrationState.Unreachable, states["1001"]);
        }

        [Fact]
        public void Contacts_for_anything_that_is_not_one_of_our_extensions_are_ignored()
        {
            var contacts = new[] { Contact("1001", "Reachable"), Contact("trunk-callcentric", "Reachable") };

            var states = RegistrationStatus.Map(contacts, new[] { "1001" });

            Assert.Equal(new[] { "1001" }, states.Keys);
        }

        /// <summary>
        /// A desk phone and a softphone can both register against one extension. If either one
        /// can take a call, the extension is registered, whichever order they arrive in.
        /// </summary>
        [Theory]
        [InlineData("Reachable", "Unreachable")]
        [InlineData("Unreachable", "Reachable")]
        public void One_reachable_contact_is_enough(string first, string second)
        {
            var contacts = new[] { Contact("1001", first), Contact("1001", second) };

            var states = RegistrationStatus.Map(contacts, new[] { "1001" });

            Assert.Equal(RegistrationState.Registered, states["1001"]);
        }

        [Fact]
        public void Every_extension_asked_about_gets_an_answer()
        {
            var states = RegistrationStatus.Map(new[] { Contact("1001", "Reachable") }, new[] { "1001", "1002", "1003" });

            Assert.Equal(3, states.Count);
        }
    }
}
