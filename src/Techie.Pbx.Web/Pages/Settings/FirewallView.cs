using Techie.Pbx.Contracts;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// Everything the firewall panel shows in one object: is the Helper there, what this system
    /// expects to be open, what was last actually applied, and whether those two agree (D142).
    ///
    /// <see cref="Error"/> carries the Helper's own sentence when something went wrong. It is a
    /// field on the view rather than an exception that escapes, because a failed ping and a
    /// refused apply are both things the page has to keep rendering around.
    /// </summary>
    public class FirewallView
    {
        /// <summary>The rules of the last apply, as they were sent. Empty when there has not been one.</summary>
        public List<FirewallRule> Applied { get; set; } = new();

        public DateTimeOffset? AppliedAtUtc { get; set; }

        /// <summary>What went wrong, in the Helper's words. Empty when nothing did.</summary>
        public string Error { get; set; } = "";

        /// <summary>What this system's settings say should be open.</summary>
        public List<FirewallRule> Expected { get; set; } = new();

        public bool HelperReachable { get; set; }

        /// <summary>Whether <see cref="Applied"/> is the same list as <see cref="Expected"/>.</summary>
        public bool InSync { get; set; }

        /// <summary>True when the Helper is there but has never applied a ruleset on this machine.</summary>
        public bool NeverApplied => this.HelperReachable && this.AppliedAtUtc == null;

        /// <summary>The protocol version ping answered with, for the reachable badge.</summary>
        public int Version { get; set; }
    }
}
