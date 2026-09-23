using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Contracts;
using Techie.Pbx.Web.Certificates;

namespace Techie.Pbx.Web.Services
{
    /// <summary>
    /// What this system expects its firewall to allow, from what it is actually configured to
    /// listen on (D142). A pure function of the settings, so the firewall page can show the
    /// expected ruleset next to the applied one and say whether they match.
    ///
    /// Every number here comes from the code that decides it and is written down nowhere else:
    /// the SIP ports from the Sip.* settings the pjsip transport is rendered from, the media
    /// range from <see cref="RtpConfRenderer"/>, and the web ports from
    /// <see cref="WebBindings"/>. A firewall that disagrees with what is listening is the bug
    /// this avoids by construction.
    ///
    /// The Helper's four safety rules — loopback, established, ICMP and SSH — are deliberately
    /// not here. They are not the web application's to send, and a message cannot remove them
    /// (D143).
    /// </summary>
    public static class FirewallRulesBuilder
    {
        public static List<FirewallRule> Build(IReadOnlyDictionary<string, string> settings)
        {
            var transport = AsteriskSettings.Transport(settings);

            var rules = new List<FirewallRule>
            {
                FirewallRule.Port(FirewallProtocol.Udp, transport.Port, "SIP UDP"),
            };

            // Unset means the TCP transport is not rendered at all (D70), and a hole for a port
            // nothing is listening on is exactly the surface area this project does not keep.
            if (transport.TcpPort != null)
                rules.Add(FirewallRule.Port(FirewallProtocol.Tcp, transport.TcpPort.Value, "SIP TCP"));

            // TLS is the same as far as the firewall is concerned: no port stored, no rule. What
            // decides whether the transport exists is the certificate (D101), but nothing listens
            // on a port nobody has named either way.
            if (transport.TlsPort != null)
                rules.Add(FirewallRule.Port(FirewallProtocol.Tcp, transport.TlsPort.Value, "SIP TLS"));

            // The media range, from the constants rtp.conf is rendered from.
            rules.Add(new FirewallRule(FirewallProtocol.Udp, RtpConfRenderer.PortStart, RtpConfRenderer.PortEnd, "RTP UDP"));

            // All three web ports, whatever is bound right now: 443 is opened before the first
            // certificate exists, because the alternative is an admin who orders one, restarts,
            // and finds the UI unreachable until somebody remembers the firewall (D99).
            rules.Add(FirewallRule.Port(FirewallProtocol.Tcp, WebBindings.HttpPort, "Web HTTP"));
            rules.Add(FirewallRule.Port(FirewallProtocol.Tcp, WebBindings.HttpsPort, "Web HTTPS"));
            rules.Add(FirewallRule.Port(FirewallProtocol.Tcp, WebBindings.BootstrapPort, "Web bootstrap"));

            return rules;
        }

        /// <summary>
        /// Whether two rule lists say the same thing. Order matters, because the expected list is
        /// built in one fixed order and the applied one is what was sent: two lists that differ
        /// only in order came from different code, which is worth showing as out of sync.
        /// </summary>
        public static bool Match(IReadOnlyList<FirewallRule> expected, IReadOnlyList<FirewallRule> applied)
        {
            if (expected.Count != applied.Count)
                return false;

            for (var index = 0; index < expected.Count; index++)
            {
                if (expected[index].EndPort != applied[index].EndPort ||
                    expected[index].Label != applied[index].Label ||
                    expected[index].Protocol != applied[index].Protocol ||
                    expected[index].StartPort != applied[index].StartPort)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
