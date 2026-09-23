using Techie.Pbx.Contracts;

namespace Techie.Pbx.Tests.Contracts
{
    /// <summary>
    /// What a firewall rule will and will not accept. Both ends of the socket run exactly this
    /// validation, so what is proved here is proved for the Helper too (D142).
    /// </summary>
    public class FirewallRuleTests
    {
        private static FirewallRule Sip() => FirewallRule.Port(FirewallProtocol.Udp, 5060, "SIP UDP");

        [Fact]
        public void A_good_rule_has_no_errors()
        {
            Assert.Empty(Sip().Validate());
        }

        [Fact]
        public void A_range_is_allowed_and_reads_as_one()
        {
            var rule = new FirewallRule(FirewallProtocol.Udp, 10000, 20000, "RTP UDP");

            Assert.Empty(rule.Validate());
            Assert.Equal("10000-20000", rule.PortRange);
        }

        [Fact]
        public void A_single_port_reads_as_the_number_alone()
        {
            Assert.Equal("5060", Sip().PortRange);
        }

        /// <summary>
        /// The enum has no zero member, so a message that left the protocol out arrives as 0 and
        /// is refused rather than quietly treated as TCP.
        /// </summary>
        [Fact]
        public void An_unset_protocol_is_refused()
        {
            var rule = new FirewallRule { EndPort = 5060, Label = "SIP", StartPort = 5060 };

            Assert.Contains(rule.Validate(), error => error.Contains("Protocol"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(65536)]
        [InlineData(100000)]
        public void A_port_outside_1_to_65535_is_refused(int port)
        {
            var rule = new FirewallRule(FirewallProtocol.Tcp, port, port, "Silly");

            Assert.NotEmpty(rule.Validate());
        }

        [Fact]
        public void A_range_that_runs_backwards_is_refused()
        {
            var rule = new FirewallRule(FirewallProtocol.Udp, 20000, 10000, "Backwards");

            Assert.Contains(rule.Validate(), error => error.Contains("above the end port"));
        }

        [Fact]
        public void An_empty_label_is_refused()
        {
            var rule = new FirewallRule(FirewallProtocol.Udp, 5060, 5060, "");

            Assert.Contains(rule.Validate(), error => error.Contains("Label"));
        }

        [Fact]
        public void A_label_over_forty_characters_is_refused()
        {
            var rule = new FirewallRule(FirewallProtocol.Udp, 5060, 5060, new string('a', FirewallRule.MaxLabelLength + 1));

            Assert.Contains(rule.Validate(), error => error.Contains("Label"));
        }

        /// <summary>
        /// The label is written into the generated ruleset inside a quoted nft comment, so
        /// anything that could end that comment — or the line, or the file — is refused rather
        /// than escaped.
        /// </summary>
        [Theory]
        [InlineData("SIP \"UDP\"")]
        [InlineData("SIP\nUDP")]
        [InlineData("SIP; drop")]
        [InlineData("SIP}")]
        [InlineData("SIP\tUDP")]
        public void A_label_outside_the_allowed_characters_is_refused(string label)
        {
            var rule = new FirewallRule(FirewallProtocol.Udp, 5060, 5060, label);

            Assert.Contains(rule.Validate(), error => error.Contains("letters, digits"));
        }

        [Fact]
        public void Spaces_dashes_and_slashes_are_allowed()
        {
            var rule = new FirewallRule(FirewallProtocol.Tcp, 443, 443, "Web HTTPS / admin UI - 443");

            Assert.Empty(rule.Validate());
        }
    }
}
