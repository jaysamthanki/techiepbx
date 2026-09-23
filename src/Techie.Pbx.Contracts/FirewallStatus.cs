namespace Techie.Pbx.Contracts
{
    /// <summary>
    /// What the Helper last applied, and when. Deliberately not "what the kernel has now": the
    /// Helper reports what it did, and the comparison against what the system currently expects
    /// happens in the web app, which is the only end that knows what the expected ruleset is
    /// (D142). One less thing for a root process to parse.
    ///
    /// <see cref="AppliedAtUtc"/> is null on a system where no apply has ever happened, which is
    /// how the firewall page tells "never applied" from "applied and out of date".
    /// </summary>
    public class FirewallStatus
    {
        public DateTimeOffset? AppliedAtUtc { get; set; }

        /// <summary>The rules of the last apply, exactly as they were sent. The safety rules the Helper adds are not here: nothing sent them.</summary>
        public List<FirewallRule> AppliedRules { get; set; } = new();
    }
}
