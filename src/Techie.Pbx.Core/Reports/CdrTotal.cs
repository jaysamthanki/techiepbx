namespace Techie.Pbx.Core.Reports
{
    /// <summary>One line of the totals: an extension or a trunk, and how its call records went.</summary>
    public class CdrTotal
    {
        public int Answered { get; set; }

        /// <summary>
        /// Records that came to this extension, or in over this trunk, and were not answered. A call
        /// an extension made that nobody picked up is not a call it missed, so it is in
        /// <see cref="Total"/> and not here.
        /// </summary>
        public int Missed { get; set; }

        /// <summary>The extension number or the trunk name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Every record this extension or trunk was on, either side.</summary>
        public int Total { get; set; }
    }
}
