using System.Net;
using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// A desk phone that provisions itself from us: DHCP option 160 points it at the provisioning
    /// endpoint, it asks for its own MAC address, and what it gets back is generated from this row
    /// and the keys assigned to it (D77).
    ///
    /// Which extension it registers as is <b>not</b> here: it is the phone's line key, and
    /// <see cref="PhoneButton.LineNumber"/> is what asks (schema 020). A phone whose keys say
    /// nothing is a phone on a desk that nobody has started using yet.
    ///
    /// Most of these fields are written by the phone rather than by an admin. Model, Firmware,
    /// LastIP and LastConfig come from the request that asked for the config, which is why they are
    /// validated as strictly as anything typed into a form: they arrive from the network.
    /// </summary>
    public partial class Phone
    {
        /// <summary>
        /// The lowest local SIP port a phone is given. Below 1024 is privileged on the phone's own
        /// stack, and nothing below it is worth handing out.
        /// </summary>
        public const int LocalPortBase = 1024;

        /// <summary>
        /// How many ports there are above <see cref="LocalPortBase"/>, i.e. the rest of the range.
        /// The port is derived from the PhoneID so that two phones behind one NAT never choose the
        /// same source port and get each other's calls (D81).
        /// </summary>
        public const int LocalPortRange = 64512;

        /// <summary>The vendor this phone is, e.g. <see cref="PhoneBrand.Polycom"/> (D88).</summary>
        public string Brand { get; set; } = PhoneBrand.Polycom;

        public bool Enabled { get; set; } = true;

        /// <summary>The firmware version its User-Agent claimed at the last config fetch.</summary>
        public string Firmware { get; set; } = "";

        /// <summary>When it last fetched its config, as "2026-09-17 09:31:02Z". Empty means never.</summary>
        public string LastConfig { get; set; } = "";

        /// <summary>The address it last asked from. Empty means it has never asked.</summary>
        public string LastIP { get; set; } = "";

        /// <summary>
        /// The port this phone is told to speak SIP from. Deterministic from the ID rather than
        /// the 5060 every phone would otherwise use, so a site with several phones behind one NAT
        /// does not have them collide (D81).
        /// </summary>
        public int LocalSipPort => LocalPortBase + (int)(this.PhoneID % LocalPortRange);

        /// <summary>The MAC address, twelve lowercase hex digits and no separators.</summary>
        public string Mac { get; set; } = "";

        /// <summary>
        /// The model its User-Agent claimed, e.g. "VVX_410". A phone whose model stops matching
        /// this is refused rather than served somebody else's config (D78).
        /// </summary>
        public string Model { get; set; } = "";

        /// <summary>What an admin calls it, e.g. "Reception". Empty until somebody names it.</summary>
        public string Name { get; set; } = "";

        public long PhoneID { get; set; }

        /// <summary>
        /// Twelve lowercase hex digits. Public because the provisioning endpoint decides whether a
        /// requested file name is a MAC address at all, and two places deciding that is one too
        /// many.
        /// </summary>
        public static bool IsValidMac(string mac) => MacPattern().IsMatch(mac);

        /// <summary>
        /// A MAC as a person might write it — "00:04:F2:AA:BB:CC", "00-04-f2-aa-bb-cc" — reduced to
        /// the one form this system stores. Anything else is left alone so that validation can
        /// report it rather than this quietly turning it into something else.
        /// </summary>
        public static string NormalizeMac(string mac)
        {
            var digits = new string(mac.Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant();

            return IsValidMac(digits) ? digits : mac.Trim();
        }

        /// <summary>
        /// Whether a request claiming this MAC is the same brand this row was created as. A MAC
        /// already known as one brand must not be silently served by the other brand's controller
        /// (D88) — this is the same "somebody is claiming this MAC" concern MatchesModel covers.
        /// </summary>
        public bool MatchesBrand(string brand) => string.Equals(this.Brand, brand, StringComparison.Ordinal);

        /// <summary>
        /// Whether the model a User-Agent is now claiming is the model this MAC was recorded with.
        /// A mismatch means either somebody is claiming another phone's MAC address or the handset
        /// on that desk has been swapped, and both want an admin rather than a config file (D78).
        ///
        /// A phone with no model recorded matches anything: that is a row created before the phone
        /// ever spoke, and the first request is what fills it in.
        /// </summary>
        public bool MatchesModel(string reportedModel) =>
            this.Model.Length == 0 || string.Equals(this.Model, reportedModel, StringComparison.OrdinalIgnoreCase);

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (!IsValidMac(this.Mac))
                errors.Add("MAC address must be 12 hex digits, lower case, with no separators.");

            if (!PhoneBrand.IsKnown(this.Brand))
                errors.Add("Brand must be Polycom or Yealink.");

            if (this.Name.Length > 64)
                errors.Add("Name must be 64 characters or fewer.");
            else if (this.Name.Length > 0 && !NamePattern().IsMatch(this.Name))
                errors.Add("Name may only contain letters, digits, spaces and . , ' - _ ( ) &");

            // Model and firmware are whatever the phone's User-Agent said, so they are held to the
            // shape PolycomUserAgent will accept and nothing wider.
            if (this.Model.Length > 0 && !ModelPattern().IsMatch(this.Model))
                errors.Add("Model may only contain letters, digits, underscores, dots and dashes.");

            if (this.Firmware.Length > 0 && !FirmwarePattern().IsMatch(this.Firmware))
                errors.Add("Firmware version may only contain letters, digits and dots.");

            if (this.LastIP.Length > 0 && !IPAddress.TryParse(this.LastIP, out _))
                errors.Add("Last IP must be an IP address.");

            if (this.LastConfig.Length > 32)
                errors.Add("Last contact must be 32 characters or fewer.");

            return errors;
        }

        [GeneratedRegex(@"^[0-9A-Za-z.]{1,32}\z")]
        private static partial Regex FirmwarePattern();

        [GeneratedRegex(@"^[0-9a-f]{12}\z")]
        private static partial Regex MacPattern();

        [GeneratedRegex(@"^[0-9A-Za-z_.\-]{1,32}\z")]
        private static partial Regex ModelPattern();

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex NamePattern();
    }
}
