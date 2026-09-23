namespace Techie.Pbx.Contracts
{
    /// <summary>
    /// One port or port range the firewall lets in, and what it is for. This is the only thing a
    /// firewall.apply message can say: no addresses, no interfaces, no actions, no chains — the
    /// Helper decides all of that (D142, D143). A message can widen what is accepted and nothing
    /// else, which is why an admin UI can be trusted with it at all.
    ///
    /// <see cref="Label"/> is not decoration: it is written into the generated ruleset as the
    /// rule's nft comment, so <c>nft list ruleset</c> on the box says what each open port is for.
    /// That is also why its character set is so narrow — it ends up inside a quoted string in a
    /// file the Helper hands to nft.
    /// </summary>
    public class FirewallRule
    {
        /// <summary>Long enough for "Provisioning HTTPS", short enough for an nft comment.</summary>
        public const int MaxLabelLength = 40;

        public const int MaxPort = 65535;
        public const int MinPort = 1;

        public int EndPort { get; set; }

        /// <summary>What the rule is for, e.g. "SIP UDP". Letters, digits, space, dash and slash only.</summary>
        public string Label { get; set; } = "";

        /// <summary>
        /// How the ports read in nft: a single number when the range is one port, and
        /// <c>start-end</c> when it is not. Here rather than in the renderer because it is a
        /// property of the rule, and because the tests that check a range check it here.
        /// </summary>
        public string PortRange => this.StartPort == this.EndPort
            ? this.StartPort.ToString()
            : $"{this.StartPort}-{this.EndPort}";

        public FirewallProtocol Protocol { get; set; }

        public int StartPort { get; set; }

        public FirewallRule()
        {
        }

        public FirewallRule(FirewallProtocol protocol, int startPort, int endPort, string label)
        {
            this.EndPort = endPort;
            this.Label = label;
            this.Protocol = protocol;
            this.StartPort = startPort;
        }

        /// <summary>A rule for a single port, which is most of them.</summary>
        public static FirewallRule Port(FirewallProtocol protocol, int port, string label) =>
            new(protocol, port, port, label);

        /// <summary>
        /// Everything wrong with this rule, in sentences. Both ends run it: the web side so a bad
        /// rule never leaves, and the Helper so a bad rule never arrives — the Helper trusting its
        /// caller is exactly the thing this design does not do.
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (this.Protocol != FirewallProtocol.Tcp && this.Protocol != FirewallProtocol.Udp)
                errors.Add("Protocol must be tcp or udp.");

            if (this.StartPort is < MinPort or > MaxPort)
                errors.Add($"Start port must be between {MinPort} and {MaxPort}.");

            if (this.EndPort is < MinPort or > MaxPort)
                errors.Add($"End port must be between {MinPort} and {MaxPort}.");

            if (this.StartPort > this.EndPort)
                errors.Add("Start port must not be above the end port.");

            if (this.Label.Length is 0 or > MaxLabelLength)
                errors.Add($"Label must be 1 to {MaxLabelLength} characters.");
            else if (!this.Label.All(IsLabelCharacter))
                errors.Add("Label may only contain letters, digits, spaces, dashes and slashes.");

            return errors;
        }

        /// <summary>
        /// An allowlist, not a denylist. Everything here is safe inside the quoted nft comment the
        /// renderer puts it in; anything else — a quote, a newline, a brace — is refused rather
        /// than escaped, because the only thing an escape can do here is be got wrong.
        /// </summary>
        private static bool IsLabelCharacter(char value) =>
            char.IsAsciiLetterOrDigit(value) || value is ' ' or '-' or '/';
    }
}
