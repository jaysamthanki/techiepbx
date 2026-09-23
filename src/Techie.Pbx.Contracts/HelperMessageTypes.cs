namespace Techie.Pbx.Contracts
{
    /// <summary>
    /// Every message the Helper answers, and there will never be a "run this" among them (D142).
    /// Naming them here rather than writing the strings at each end is what makes
    /// <see cref="HelperRequest.Validate"/> able to say "that is not a message" before anything
    /// looks at the rest of the object.
    /// </summary>
    public static class HelperMessageTypes
    {
        /// <summary>Replace the firewall ruleset with the safety rules plus the rules carried.</summary>
        public const string FirewallApply = "firewall.apply";

        /// <summary>What the Helper last applied, and when. Reads nothing from the kernel.</summary>
        public const string FirewallStatus = "firewall.status";

        /// <summary>Is the Helper there, and what version of this protocol does it speak.</summary>
        public const string Ping = "ping";

        public static bool IsKnown(string type) =>
            type is FirewallApply or FirewallStatus or Ping;
    }
}
