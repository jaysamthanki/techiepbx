using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// What a Yealink phone says about itself when it fetches a provisioning file. The header
    /// looks like this, and this is the only shape the Yealink provisioning endpoint answers
    /// (D88, mirroring D78):
    ///
    /// <code>
    /// Yealink SIP-T33G 124.86.0.118 24:9a:d8:1e:83:fa
    /// </code>
    ///
    /// "Yealink", the model as "SIP-" plus the part worth storing, the firmware, then the phone's
    /// own MAC address, colon separated. Unlike a Polycom header the MAC is right there, which is
    /// also why it is not available at the boot request (D89): the boot file name is fixed, and
    /// the MAC only becomes known once this header is parsed.
    ///
    /// A User-Agent this cannot read gets 403 rather than a config file: provisioning credentials
    /// that leaked are worth far less to a scanner if the thing holding them also has to sound
    /// like a desk phone. It is a filter and not a security boundary — a header is trivially
    /// forged — which is why the credentials are still the gate.
    /// </summary>
    public partial class YealinkUserAgent
    {
        /// <summary>The firmware version, e.g. "124.86.0.118".</summary>
        public string Firmware { get; }

        /// <summary>The MAC address, normalised to twelve lowercase hex digits, no separators.</summary>
        public string Mac { get; }

        /// <summary>
        /// The model, e.g. "T33G": the part after "SIP-", which is what stays the same across
        /// firmware upgrades and is what makes it worth storing and comparing against.
        /// </summary>
        public string Model { get; }

        private YealinkUserAgent(string model, string firmware, string mac)
        {
            this.Firmware = firmware;
            this.Mac = mac;
            this.Model = model;
        }

        /// <summary>
        /// Reads a User-Agent header. False means "not a Yealink phone as far as we can tell",
        /// which is the answer for a missing header, a browser, or anything else that asks.
        /// </summary>
        public static bool TryParse(string? userAgent, out YealinkUserAgent parsed)
        {
            parsed = new YealinkUserAgent("", "", "");

            if (string.IsNullOrWhiteSpace(userAgent) || userAgent.Length > 256)
                return false;

            var match = UserAgentPattern().Match(userAgent);
            if (!match.Success)
                return false;

            var mac = match.Groups["mac"].Value.Replace(":", "", StringComparison.Ordinal).ToLowerInvariant();

            parsed = new YealinkUserAgent(match.Groups["model"].Value, match.Groups["firmware"].Value, mac);
            return true;
        }

        /// <summary>
        /// Anchored at the start, so a Yealink-looking tail on somebody else's header does not
        /// get in. The model is restricted to "T" followed by digits and an optional trailing
        /// letter — T33G, T46S, T54W — which is the shape every real Yealink desk phone model
        /// takes.
        /// </summary>
        [GeneratedRegex(@"^Yealink\s+SIP-(?<model>T\d+[A-Z]?)\s+(?<firmware>[0-9][0-9.]{0,31})\s+(?<mac>[0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})")]
        private static partial Regex UserAgentPattern();
    }
}
