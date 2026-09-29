using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Contracts;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Services;

namespace Techie.Pbx.Tests.Web
{
    /// <summary>
    /// What this system expects its firewall to allow, from the settings that decide what is
    /// listening (D142). The point of these tests is that the two can never drift: a port that
    /// nothing listens on gets no hole, and a port that does gets one.
    /// </summary>
    public class FirewallRulesBuilderTests
    {
        private static Dictionary<string, string> Settings(params (string Key, string Value)[] values) =>
            values.ToDictionary(pair => pair.Key, pair => pair.Value);

        /// <summary>Most rules do not care about the extensions, so most tests pass none.</summary>
        private static List<FirewallRule> Build(Dictionary<string, string> settings) =>
            FirewallRulesBuilder.Build(settings, new List<Extension>());

        private static FirewallRule Rule(IEnumerable<FirewallRule> rules, string label) =>
            rules.Single(rule => rule.Label == label);

        private static List<Extension> WebClientExtensions() => new()
        {
            new Extension { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111", WebClient = true },
        };

        [Fact]
        public void A_bare_system_opens_sip_udp_rtp_and_the_three_web_ports()
        {
            var rules = Build(Settings());

            Assert.Equal(
                new[] { "SIP UDP", "RTP UDP", "Web HTTP", "Web HTTPS", "Web bootstrap" },
                rules.Select(rule => rule.Label).ToArray());
        }

        [Fact]
        public void Sip_udp_follows_the_configured_port()
        {
            var rules = Build(Settings((SettingsKeys.SipPort, "5070")));
            var sip = Rule(rules, "SIP UDP");

            Assert.Equal(FirewallProtocol.Udp, sip.Protocol);
            Assert.Equal(5070, sip.StartPort);
            Assert.Equal(5070, sip.EndPort);
        }

        [Fact]
        public void Sip_udp_defaults_to_5060_when_nothing_is_stored()
        {
            Assert.Equal(PjsipTransport.DefaultPort, Rule(Build(Settings()), "SIP UDP").StartPort);
        }

        /// <summary>Unset means no TCP transport is rendered at all (D70), so there is nothing to open.</summary>
        [Fact]
        public void Sip_tcp_is_left_out_when_no_tcp_port_is_set()
        {
            Assert.DoesNotContain(Build(Settings()), rule => rule.Label == "SIP TCP");
        }

        [Fact]
        public void Sip_tcp_appears_when_a_tcp_port_is_set()
        {
            var tcp = Rule(Build(Settings((SettingsKeys.SipTcpPort, "5060"))), "SIP TCP");

            Assert.Equal(FirewallProtocol.Tcp, tcp.Protocol);
            Assert.Equal(5060, tcp.StartPort);
        }

        [Fact]
        public void Sip_tls_is_left_out_when_no_tls_port_is_set()
        {
            Assert.DoesNotContain(Build(Settings()), rule => rule.Label == "SIP TLS");
        }

        [Fact]
        public void Sip_tls_appears_when_a_tls_port_is_set()
        {
            var tls = Rule(Build(Settings((SettingsKeys.SipTlsPort, "5061"))), "SIP TLS");

            Assert.Equal(FirewallProtocol.Tcp, tls.Protocol);
            Assert.Equal(5061, tls.StartPort);
        }

        /// <summary>A blank stored value is "not set", the same way every other setting reads it.</summary>
        [Fact]
        public void A_blank_tls_port_counts_as_unset()
        {
            Assert.DoesNotContain(
                Build(Settings((SettingsKeys.SipTlsPort, "   "))),
                rule => rule.Label == "SIP TLS");
        }

        /// <summary>
        /// The web client's ws/wss ports open exactly while http.conf is enabled, which is while
        /// an enabled extension has the web client (D159) — the same gate, one definition.
        /// </summary>
        [Fact]
        public void The_websocket_ports_open_only_while_an_extension_has_the_web_client()
        {
            Assert.DoesNotContain(Build(Settings()), rule => rule.Label is "SIP WS" or "SIP WSS");

            var rules = FirewallRulesBuilder.Build(Settings(), WebClientExtensions());

            Assert.Equal(HttpConfRenderer.WsPort, Rule(rules, "SIP WS").StartPort);
            Assert.Equal(FirewallProtocol.Tcp, Rule(rules, "SIP WS").Protocol);
            Assert.Equal(HttpConfRenderer.WssPort, Rule(rules, "SIP WSS").StartPort);
            Assert.Equal(FirewallProtocol.Tcp, Rule(rules, "SIP WSS").Protocol);
        }

        /// <summary>A switched-off extension renders no endpoint and opens no port either.</summary>
        [Fact]
        public void A_disabled_web_client_extension_opens_nothing()
        {
            var extensions = WebClientExtensions();
            extensions[0].Enabled = false;

            Assert.DoesNotContain(
                FirewallRulesBuilder.Build(Settings(), extensions),
                rule => rule.Label is "SIP WS" or "SIP WSS");
        }

        /// <summary>The media range is the renderer's constants, not numbers typed again here.</summary>
        [Fact]
        public void Rtp_is_the_range_rtp_conf_is_rendered_from()
        {
            var rtp = Rule(Build(Settings()), "RTP UDP");

            Assert.Equal(FirewallProtocol.Udp, rtp.Protocol);
            Assert.Equal(RtpConfRenderer.PortStart, rtp.StartPort);
            Assert.Equal(RtpConfRenderer.PortEnd, rtp.EndPort);
        }

        [Fact]
        public void Every_rule_built_is_a_rule_the_helper_would_accept()
        {
            var rules = FirewallRulesBuilder.Build(
                Settings(
                    (SettingsKeys.SipPort, "5060"),
                    (SettingsKeys.SipTcpPort, "5060"),
                    (SettingsKeys.SipTlsPort, "5061")),
                WebClientExtensions());

            Assert.Empty(HelperRequest.FirewallApply(rules).Validate());
        }

        [Fact]
        public void Two_identical_lists_are_in_sync()
        {
            var expected = Build(Settings());
            var applied = Build(Settings());

            Assert.True(FirewallRulesBuilder.Match(expected, applied));
        }

        [Fact]
        public void A_changed_port_is_out_of_sync()
        {
            var expected = Build(Settings((SettingsKeys.SipPort, "5060")));
            var applied = Build(Settings((SettingsKeys.SipPort, "5070")));

            Assert.False(FirewallRulesBuilder.Match(expected, applied));
        }

        [Fact]
        public void An_extra_rule_is_out_of_sync()
        {
            var expected = Build(Settings());
            var applied = Build(Settings((SettingsKeys.SipTlsPort, "5061")));

            Assert.False(FirewallRulesBuilder.Match(expected, applied));
            Assert.False(FirewallRulesBuilder.Match(applied, expected));
        }

        /// <summary>Nothing applied is not "in sync with nothing": it is out of sync.</summary>
        [Fact]
        public void An_empty_applied_list_is_out_of_sync()
        {
            Assert.False(FirewallRulesBuilder.Match(Build(Settings()), new List<FirewallRule>()));
        }
    }
}
