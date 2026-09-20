using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Trunk validation. Everything here becomes a PJSIP section name, a URI or a password in a
    /// conf file, so the rules are the first line of defence and worth being exact about.
    /// </summary>
    public class TrunkTests
    {
        private static Trunk Valid() => new()
        {
            Name = "provider",
            ServerHost = "sip.example.com",
            Username = "17771234567",
            Password = "not-a-real-password",
            Register = true,
            Codecs = "ulaw,alaw",
            MatchAddresses = "203.0.113.0/24",
        };

        [Fact]
        public void A_trunk_filled_in_properly_validates()
        {
            Assert.Empty(Valid().Validate());
        }

        /// <summary>
        /// The name becomes [name] in pjsip.conf, where extensions already have [1001]. Starting
        /// with a letter is what keeps a trunk from colliding with an extension (D37).
        /// </summary>
        [Theory]
        [InlineData("provider", true)]
        [InlineData("Call-Centric-2", true)]
        [InlineData("a", true)]
        [InlineData("1001", false)]
        [InlineData("2provider", false)]
        [InlineData("-provider", false)]
        [InlineData("my provider", false)]
        [InlineData("provider_1", false)]
        [InlineData("provider.com", false)]
        [InlineData("", false)]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
        public void A_name_has_to_be_safe_as_a_section_name(string name, bool valid)
        {
            var trunk = Valid();
            trunk.Name = name;

            Assert.Equal(valid, trunk.Validate().Count == 0);
        }

        [Theory]
        [InlineData("sip.example.com", true)]
        [InlineData("203.0.113.10", true)]
        [InlineData("a", true)]
        [InlineData("", false)]
        [InlineData("sip example.com", false)]
        [InlineData("sip.example.com;deny", false)]
        [InlineData("[sip]", false)]
        public void A_server_host_has_to_look_like_a_host(string host, bool valid)
        {
            var trunk = Valid();
            trunk.ServerHost = host;

            Assert.Equal(valid, trunk.Validate().Count == 0);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(65536)]
        [InlineData(-1)]
        public void A_server_port_outside_the_range_is_refused(int port)
        {
            var trunk = Valid();
            trunk.ServerPort = port;

            Assert.Contains("port", string.Join(" ", trunk.Validate()));
        }

        /// <summary>
        /// Registering means proving who we are, which needs something to prove it with.
        /// </summary>
        [Fact]
        public void A_trunk_that_registers_needs_a_username_and_a_password()
        {
            var trunk = Valid();
            trunk.Username = "";
            trunk.Password = "";

            var errors = trunk.Validate();

            Assert.Contains(errors, e => e.Contains("username"));
            Assert.Contains(errors, e => e.Contains("password"));
        }

        [Fact]
        public void A_trunk_that_does_not_register_needs_neither()
        {
            var trunk = Valid();
            trunk.Register = false;
            trunk.Username = "";
            trunk.Password = "";

            Assert.Empty(trunk.Validate());
        }

        [Fact]
        public void A_password_without_a_username_to_use_it_with_is_refused()
        {
            var trunk = Valid();
            trunk.Register = false;
            trunk.Username = "";

            Assert.Contains("username", string.Join(" ", trunk.Validate()));
        }

        [Fact]
        public void The_auth_username_stands_in_for_the_username_when_it_is_set()
        {
            var trunk = Valid();
            Assert.Equal("17771234567", trunk.EffectiveAuthUsername);

            trunk.AuthUsername = "17771234567-auth";
            Assert.Equal("17771234567-auth", trunk.EffectiveAuthUsername);
        }

        /// <summary>
        /// A provider chooses the password, so the rule is only "nothing that breaks a conf file".
        /// </summary>
        [Theory]
        [InlineData("plain", true)]
        [InlineData("P@ssw0rd!#%^&*()", true)]
        [InlineData("has space", false)]
        [InlineData("semi;colon", false)]
        [InlineData("bracket]", false)]
        [InlineData("quote\"", false)]
        [InlineData("new\nline", false)]
        public void A_password_may_be_anything_that_cannot_break_out_of_the_file(string password, bool valid)
        {
            var trunk = Valid();
            trunk.Password = password;

            Assert.Equal(valid, trunk.Validate().Count == 0);
        }

        /// <summary>
        /// Offering a codec modules.conf does not load would generate config that cannot work
        /// (D31), so the list is exactly what is loaded.
        /// </summary>
        [Theory]
        [InlineData("ulaw", true)]
        [InlineData("ulaw,alaw,gsm", true)]
        [InlineData(" ulaw , alaw ", true)]
        [InlineData("g722,slin", true)]
        [InlineData("g729", false)]
        [InlineData("ulaw,g729", false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        public void Codecs_have_to_be_ones_this_system_loads(string codecs, bool valid)
        {
            var trunk = Valid();
            trunk.Codecs = codecs;

            Assert.Equal(valid, trunk.Validate().Count == 0);
        }

        [Fact]
        public void The_codec_list_is_split_and_trimmed()
        {
            var trunk = Valid();
            trunk.Codecs = " ulaw , alaw ";

            Assert.Equal(new[] { "ulaw", "alaw" }, trunk.CodecList());
        }

        [Theory]
        [InlineData("203.0.113.0/24", true)]
        [InlineData("203.0.113.10", true)]
        [InlineData("203.0.113.0/24, 198.51.100.0/24", true)]
        [InlineData("", true)]
        [InlineData("sip.example.com", false)]
        [InlineData("203.0.113.0/33", false)]
        [InlineData("2001:db8::/32", false)]
        [InlineData("203.0.113.0/24, nonsense", false)]
        public void Provider_addresses_have_to_be_addresses_or_ranges(string addresses, bool valid)
        {
            var trunk = Valid();
            trunk.MatchAddresses = addresses;

            Assert.Equal(valid, trunk.Validate().Count == 0);
        }

        [Theory]
        [InlineData("17771234567", true)]
        [InlineData("+17771234567", true)]
        [InlineData("1", false)]
        [InlineData("077 1234 5678", false)]
        [InlineData("anonymous", false)]
        public void A_caller_id_number_is_a_number(string number, bool valid)
        {
            var trunk = Valid();
            trunk.CallerIDNumber = number;

            Assert.Equal(valid, trunk.Validate().Count == 0);
        }

        [Fact]
        public void The_inbound_context_is_the_trunk_name_with_a_prefix()
        {
            Assert.Equal("from-trunk-provider", Valid().Context);
        }
    }
}
