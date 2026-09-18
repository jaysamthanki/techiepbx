using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// What a Polycom phone says about itself when it fetches a provisioning file. The header looks
    /// like this, and this is the only shape the provisioning endpoint answers (D78):
    ///
    /// <code>
    /// FileTransport PolycomVVX-VVX_410-UA/5.9.5.0614 Type/Application
    /// FileTransport PolycomSoundStationIP-SSIP_5000-UA/4.0.9.1400
    /// FileTransport PolyEdge-Edge_E350-UA/8.0.0.1234
    /// </code>
    ///
    /// A User-Agent this cannot read gets 403 rather than a config file: provisioning credentials
    /// that leaked are worth far less to a scanner if the thing holding them also has to sound like
    /// a desk phone. It is a filter and not a security boundary — a header is trivially forged —
    /// which is why the credentials are still the gate.
    /// </summary>
    public partial class PolycomUserAgent
    {
        /// <summary>The firmware version, e.g. "5.9.5.0614".</summary>
        public string Firmware { get; }

        /// <summary>
        /// The model, e.g. "VVX_410" or "SSIP_5000": the part that stays the same across firmware
        /// upgrades, which is what makes it worth storing and comparing against.
        /// </summary>
        public string Model { get; }

        private PolycomUserAgent(string model, string firmware)
        {
            this.Firmware = firmware;
            this.Model = model;
        }

        /// <summary>
        /// Reads a User-Agent header. False means "not a Polycom phone as far as we can tell",
        /// which is the answer for a missing header, a browser, or anything else that asks.
        /// </summary>
        public static bool TryParse(string? userAgent, out PolycomUserAgent parsed)
        {
            parsed = new PolycomUserAgent("", "");

            if (string.IsNullOrWhiteSpace(userAgent) || userAgent.Length > 256)
                return false;

            var match = UserAgentPattern().Match(userAgent);
            if (!match.Success)
                return false;

            parsed = new PolycomUserAgent(match.Groups["model"].Value, match.Groups["firmware"].Value);
            return true;
        }

        /// <summary>
        /// Anchored at the start, because the interesting part is the front of the header and
        /// anything trailing ("Type/Application") is the phone's business. Both vendor spellings
        /// are accepted: the older Polycom names and the PolyEdge ones (D78).
        /// </summary>
        [GeneratedRegex(@"^FileTransport\s+(?:Polycom|PolyEdge)[A-Za-z0-9]*-(?<model>[A-Za-z0-9_]{1,32})-UA/(?<firmware>[0-9][0-9A-Za-z.]{0,31})")]
        private static partial Regex UserAgentPattern();
    }
}
