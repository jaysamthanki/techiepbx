namespace Techie.Pbx.Asterisk.Status
{
    /// <summary>
    /// One thing that needs attention, in a sentence an admin can act on. The whole of what the
    /// status page's "Needs attention" list is made of.
    ///
    /// The text is written here rather than in the page because the rule and its wording are the
    /// same thought: a test that pins down the rule pins down what it says as well.
    /// </summary>
    public class Finding
    {
        public FindingSeverity Severity { get; set; }

        /// <summary>What this is about, which is how the page decides where to link (D35's rule
        /// about references, applied to pages: name the thing, not the route to it).</summary>
        public FindingSubject Subject { get; set; }

        /// <summary>One complete sentence, including what to do about it where that is not obvious.</summary>
        public string Text { get; set; } = "";
    }
}
