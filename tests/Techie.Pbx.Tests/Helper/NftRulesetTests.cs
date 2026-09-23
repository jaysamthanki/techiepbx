using Techie.Pbx.Contracts;
using Techie.Pbx.Helper.Firewall;

namespace Techie.Pbx.Tests.Helper
{
    /// <summary>
    /// The generated nftables ruleset (D143). A pure function with a golden file, for the same
    /// reason the Asterisk renderers have them: this text is fed to nft by a root process, so
    /// what it says has to be reviewable as text rather than inferred from behaviour.
    ///
    /// There is no test here that runs nft or touches a socket — those are lab-verified.
    /// </summary>
    public class NftRulesetTests
    {
        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        /// <summary>The rules the firewall page sends on a system with SIP TCP and a certificate.</summary>
        private static List<FirewallRule> SampleRules() => new()
        {
            FirewallRule.Port(FirewallProtocol.Udp, 5060, "SIP UDP"),
            FirewallRule.Port(FirewallProtocol.Tcp, 5060, "SIP TCP"),
            FirewallRule.Port(FirewallProtocol.Tcp, 5061, "SIP TLS"),
            new FirewallRule(FirewallProtocol.Udp, 10000, 20000, "RTP UDP"),
            FirewallRule.Port(FirewallProtocol.Tcp, 80, "Web HTTP"),
            FirewallRule.Port(FirewallProtocol.Tcp, 443, "Web HTTPS"),
            FirewallRule.Port(FirewallProtocol.Tcp, 8080, "Web bootstrap"),
        };

        [Fact]
        public void Matches_expected_file()
        {
            Assert.Equal(Expected("nft-ruleset.conf"), NftRuleset.Render(SampleRules()));
        }

        /// <summary>
        /// The four safety rules are written by the renderer and come before anything a message
        /// carried, so no apply can get in front of them or take them away (D142).
        /// </summary>
        [Fact]
        public void The_safety_rules_are_always_there_and_come_first()
        {
            var text = NftRuleset.Render(SampleRules());
            var lines = text.Split('\n').Select(line => line.Trim()).ToList();

            var loopback = lines.IndexOf("iif \"lo\" accept");
            var established = lines.IndexOf("ct state established,related accept");
            var icmp = lines.IndexOf("meta l4proto { icmp, ipv6-icmp } accept");
            var ssh = lines.IndexOf("tcp dport 22 accept");
            var first = lines.IndexOf("udp dport 5060 accept comment \"SIP UDP\"");

            Assert.True(loopback > 0);
            Assert.True(established > loopback);
            Assert.True(icmp > established);
            Assert.True(ssh > icmp);
            Assert.True(first > ssh);
        }

        /// <summary>An empty apply still produces the safety rules, and says so in the file.</summary>
        [Fact]
        public void An_empty_ruleset_is_still_the_safety_rules()
        {
            var text = NftRuleset.Render(new List<FirewallRule>());

            Assert.Contains("tcp dport 22 accept", text);
            Assert.Contains("policy drop;", text);
            Assert.Contains("asked for no open ports", text);
            Assert.DoesNotContain("comment", text);
        }

        /// <summary>
        /// One table, added, deleted and rebuilt, so loading this file replaces exactly our table.
        /// fail2ban's f2b-table is a table of its own and must not appear here at all (D143).
        /// </summary>
        [Fact]
        public void Only_our_own_table_is_in_the_file()
        {
            var text = NftRuleset.Render(SampleRules());

            Assert.Contains("table inet tnpbx-input\ndelete table inet tnpbx-input\n", text);

            // Nothing here flushes the whole ruleset, and no other table is touched — fail2ban's
            // included. The comment at the top names f2b-table; nothing that is not a comment does.
            Assert.DoesNotContain("flush ruleset", text);
            Assert.DoesNotContain("table inet f2b", text);
            Assert.DoesNotContain(text.Split('\n').Where(line => !line.TrimStart().StartsWith('#')), line => line.Contains("f2b"));

            // "table inet tnpbx-input" three times: the add, the delete and the definition. No
            // other table is named at all.
            Assert.Equal(3, text.Split("table inet ").Length - 1);
        }

        [Fact]
        public void The_chain_is_an_input_hook_that_drops_by_default()
        {
            Assert.Contains("chain input {", NftRuleset.Render(SampleRules()));
            Assert.Contains("type filter hook input priority 0; policy drop;", NftRuleset.Render(SampleRules()));
        }

        [Fact]
        public void A_range_is_rendered_as_a_range()
        {
            var text = NftRuleset.Render(new List<FirewallRule>
            {
                new(FirewallProtocol.Udp, 10000, 20000, "RTP UDP"),
            });

            Assert.Contains("udp dport 10000-20000 accept comment \"RTP UDP\"", text);
        }

        /// <summary>
        /// The last line of defence: a rule that got past both ends of the socket still never
        /// becomes a file, the same way ConfText.Safe stops a bad row becoming a conf section.
        /// </summary>
        [Theory]
        [InlineData(0, 0, "Zero")]
        [InlineData(70000, 70000, "Too high")]
        [InlineData(20000, 10000, "Backwards")]
        [InlineData(5060, 5060, "Quote\" accept")]
        [InlineData(5060, 5060, "")]
        public void An_invalid_rule_is_refused_rather_than_rendered(int start, int end, string label)
        {
            var rules = new List<FirewallRule> { new(FirewallProtocol.Tcp, start, end, label) };

            Assert.Throws<InvalidOperationException>(() => NftRuleset.Render(rules));
        }
    }
}
