using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What a trunk turns into: PJSIP sections in pjsip.conf and an inbound context in the
    /// dialplan. The golden files are a registering trunk of the shape a provider's own guide
    /// describes, and an IP-authenticated one that does not register (D39).
    /// </summary>
    public class TrunkRendererTests
    {
        private static List<Trunk> SampleTrunks() => new()
        {
            // Out of order on purpose, plus a disabled one that must not be rendered.
            new Trunk
            {
                Name = "ip-provider",
                ServerHost = "sip.example.net",
                ServerPort = 5080,
                Register = false,
                Codecs = "ulaw",
                MatchAddresses = "203.0.113.0/24",
            },
            new Trunk
            {
                Name = "zz-disabled",
                ServerHost = "sip.example.org",
                Username = "someone",
                Password = "not-a-real-password",
                Enabled = false,
            },
            new Trunk
            {
                Name = "callcentric",
                ServerHost = "callcentric.com",
                Username = "17771234567",
                Password = "not-a-real-password",
                Register = true,
                CallerIDName = "Techie Networks",
                CallerIDNumber = "17771234567",
                Codecs = "ulaw,alaw",
                MatchAddresses = "204.11.192.0/23, 204.11.194.0/23",
            },
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        private static string Pjsip(params Trunk[] trunks) =>
            PjsipConfRenderer.Render(new PjsipTransport(), new List<Extension>(), trunks);

        [Fact]
        public void Pjsip_trunks_match_expected_file()
        {
            var actual = PjsipConfRenderer.Render(new PjsipTransport(), new List<Extension>(), SampleTrunks());

            Assert.Equal(Expected("pjsip-trunks.conf"), actual);
        }

        [Fact]
        public void Trunk_contexts_match_expected_file()
        {
            var actual = ExtensionsConfRenderer.Render(new List<Extension>(), SampleTrunks());

            Assert.Equal(Expected("extensions-trunks.conf"), actual);
        }

        /// <summary>
        /// The config a system without trunks generates has to be the config it generated before
        /// trunks existed, or every install would show a pointless diff and a pjsip reload (D39).
        /// </summary>
        [Fact]
        public void A_system_with_no_trunks_renders_exactly_what_it_did_before()
        {
            var extensions = new List<Extension>
            {
                new() { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
            };

            Assert.Equal(
                PjsipConfRenderer.Render(new PjsipTransport(), extensions),
                PjsipConfRenderer.Render(new PjsipTransport(), extensions, new List<Trunk>()));

            Assert.Equal(
                ExtensionsConfRenderer.Render(extensions),
                ExtensionsConfRenderer.Render(extensions, new List<Trunk>()));
        }

        [Fact]
        public void A_disabled_trunk_is_not_in_the_config_at_all()
        {
            var pjsip = PjsipConfRenderer.Render(new PjsipTransport(), new List<Extension>(), SampleTrunks());
            var dialplan = ExtensionsConfRenderer.Render(new List<Extension>(), SampleTrunks());

            Assert.DoesNotContain("zz-disabled", pjsip);
            Assert.DoesNotContain("sip.example.org", pjsip);
            Assert.DoesNotContain("zz-disabled", dialplan);
        }

        [Fact]
        public void A_trunk_that_registers_gets_a_registration_and_a_qualified_aor()
        {
            var actual = Pjsip(SampleTrunks().Single(t => t.Name == "callcentric"));

            Assert.Contains("[callcentric-reg]\ntype = registration\n", actual);
            Assert.Contains("outbound_auth = callcentric-auth\n", actual);
            Assert.Contains("qualify_frequency = 60\n", actual);
            Assert.DoesNotContain("contact = sip:", actual);
        }

        /// <summary>
        /// Nothing tells the provider where we are, so the aor has to say where it is instead.
        /// </summary>
        [Fact]
        public void A_trunk_that_does_not_register_gets_a_static_contact_and_no_registration()
        {
            var actual = Pjsip(SampleTrunks().Single(t => t.Name == "ip-provider"));

            Assert.DoesNotContain("type = registration", actual);
            Assert.Contains("contact = sip:sip.example.net:5080\n", actual);
            Assert.DoesNotContain("qualify_frequency", actual);
        }

        /// <summary>The default port is the default: saying it again in every URI helps nobody.</summary>
        [Fact]
        public void The_port_is_only_written_when_it_is_not_the_default()
        {
            var trunk = SampleTrunks().Single(t => t.Name == "callcentric");

            Assert.Contains("server_uri = sip:callcentric.com\n", Pjsip(trunk));

            trunk.ServerPort = 5061;
            var moved = Pjsip(trunk);

            Assert.Contains("server_uri = sip:callcentric.com:5061\n", moved);
            Assert.Contains("client_uri = sip:17771234567@callcentric.com:5061\n", moved);
        }

        [Fact]
        public void The_auth_username_falls_back_to_the_username_and_is_used_when_it_differs()
        {
            var trunk = SampleTrunks().Single(t => t.Name == "callcentric");

            Assert.Contains("[callcentric-auth]\ntype = auth\nauth_type = userpass\nusername = 17771234567\n", Pjsip(trunk));

            trunk.AuthUsername = "17771234567-auth";

            Assert.Contains("username = 17771234567-auth\n", Pjsip(trunk));
            Assert.Contains("from_user = 17771234567\n", Pjsip(trunk));
        }

        [Fact]
        public void A_trunk_with_no_password_gets_no_auth_section()
        {
            var actual = Pjsip(SampleTrunks().Single(t => t.Name == "ip-provider"));

            Assert.DoesNotContain("type = auth", actual);
            Assert.DoesNotContain("outbound_auth", actual);
        }

        [Fact]
        public void Provider_addresses_become_one_identify_with_a_match_per_range()
        {
            var actual = Pjsip(SampleTrunks().Single(t => t.Name == "callcentric"));

            Assert.Contains("[callcentric-identify]\ntype = identify\nendpoint = callcentric\n", actual);
            Assert.Contains("match = 204.11.192.0/23\nmatch = 204.11.194.0/23\n", actual);
        }

        [Fact]
        public void A_trunk_without_provider_addresses_gets_no_identify()
        {
            var trunk = SampleTrunks().Single(t => t.Name == "callcentric");
            trunk.MatchAddresses = "";

            Assert.DoesNotContain("type = identify", Pjsip(trunk));
        }

        [Fact]
        public void Caller_id_is_only_written_when_there_is_a_number_to_write()
        {
            var trunk = SampleTrunks().Single(t => t.Name == "callcentric");
            Assert.Contains("callerid = \"Techie Networks\" <17771234567>\n", Pjsip(trunk));

            trunk.CallerIDNumber = "";
            Assert.DoesNotContain("callerid", Pjsip(trunk));
        }

        [Fact]
        public void Trunks_are_rendered_in_name_order_whatever_order_they_arrive_in()
        {
            var actual = Pjsip(SampleTrunks().Where(t => t.Enabled).ToArray());

            Assert.True(actual.IndexOf("[callcentric]", StringComparison.Ordinal) < actual.IndexOf("[ip-provider]", StringComparison.Ordinal));
        }

        /// <summary>
        /// The endpoint's context is the trunk's own, so an inbound route can tell which provider
        /// a call arrived on (D38).
        /// </summary>
        [Fact]
        public void The_endpoint_context_is_the_trunks_own_inbound_context()
        {
            var trunk = SampleTrunks().Single(t => t.Name == "callcentric");

            Assert.Equal("from-trunk-callcentric", trunk.Context);
            Assert.Contains($"context = {trunk.Context}\n", Pjsip(trunk));
            Assert.Contains($"[{trunk.Context}]\n", ExtensionsConfRenderer.Render(new List<Extension>(), new[] { trunk }));
        }

        [Fact]
        public void A_trunk_that_would_not_validate_is_never_written()
        {
            var trunk = new Trunk { Name = "broken", ServerHost = "", Register = false, Codecs = "ulaw" };

            Assert.Throws<InvalidOperationException>(() => Pjsip(trunk));
            Assert.Throws<InvalidOperationException>(() => ExtensionsConfRenderer.Render(new List<Extension>(), new[] { trunk }));
        }

        /// <summary>
        /// A row that reached the database without going through the repository still cannot open
        /// a section of its own choosing.
        /// </summary>
        [Theory]
        [InlineData("host.example.com\n[evil]\ntype = endpoint")]
        [InlineData("host.example.com; deny = all")]
        [InlineData("host]")]
        public void Injection_through_a_trunk_is_refused(string host)
        {
            var trunk = new Trunk
            {
                Name = "provider",
                ServerHost = host,
                Register = false,
                Codecs = "ulaw",
            };

            Assert.Throws<InvalidOperationException>(() => Pjsip(trunk));
        }
    }
}
