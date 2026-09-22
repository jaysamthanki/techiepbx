using Techie.Pbx.Core.Reports;

namespace Techie.Pbx.Web.Pages.Reports
{
    /// <summary>What the results partial shows: the call list, the totals, or why there are neither.</summary>
    public class ReportResults
    {
        public List<string> Errors { get; set; } = new();

        public List<CdrTotal> ExtensionTotals { get; set; } = new();

        /// <summary>How many records matched, which is more than <see cref="Rows"/> when the list was cut short.</summary>
        public int Matched { get; set; }

        public List<CallRow> Rows { get; set; } = new();

        public List<CdrTotal> TrunkTotals { get; set; } = new();

        /// <summary>Whether the list stops short of everything that matched; the totals and the CSV never do.</summary>
        public bool Truncated => this.Matched > this.Rows.Count;
    }
}
