using Techie.Pbx.Asterisk.Status;

namespace Techie.Pbx.Web.Pages.Status
{
    /// <summary>
    /// The slower half of the status page: what needs attention, and how much of everything there
    /// is. Both come from the database rather than from a poll, so this is fetched on load and
    /// again when something changed, not every five seconds.
    /// </summary>
    public class AttentionView
    {
        /// <summary>The counts strip, in the order the navbar lists the pages they link to.</summary>
        public List<CountLink> Counts { get; set; } = new();

        /// <summary>What <see cref="AttentionRules"/> found, already sorted worst first.</summary>
        public List<Finding> Findings { get; set; } = new();
    }
}
