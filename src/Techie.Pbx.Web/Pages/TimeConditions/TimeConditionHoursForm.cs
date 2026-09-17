using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.TimeConditions
{
    /// <summary>
    /// One open-hours row of the form: the weekdays it covers and the window it opens. The rows are
    /// added and removed in the browser, so each carries a <see cref="Key"/> that its field names
    /// are built from — ASP.NET Core reads the posted "hours.Index" values, which means removing a
    /// row is removing it, with nothing to renumber.
    /// </summary>
    public class TimeConditionHoursForm
    {
        /// <summary>
        /// The weekday bits that are ticked, e.g. 1, 2, 4 for Monday, Tuesday, Wednesday. Posted as
        /// one value per box and turned into a <see cref="TimeConditionRule.DaysMask"/> on save.
        /// </summary>
        public List<int> Days { get; set; } = new();

        public string? EndTime { get; set; } = "";

        /// <summary>
        /// What this row's field names are indexed by. Set by the page: the stored rows get their
        /// position, a row added in the browser gets one the page's JavaScript makes up.
        /// </summary>
        public string Key { get; set; } = "";

        public string? StartTime { get; set; } = "";

        /// <summary>The boxes, in the order they are shown, with the bit each one sets.</summary>
        public static IReadOnlyList<(string Label, int Bit)> Weekdays { get; } = new[]
        {
            ("Mon", TimeConditionRule.Monday),
            ("Tue", TimeConditionRule.Tuesday),
            ("Wed", TimeConditionRule.Wednesday),
            ("Thu", TimeConditionRule.Thursday),
            ("Fri", TimeConditionRule.Friday),
            ("Sat", TimeConditionRule.Saturday),
            ("Sun", TimeConditionRule.Sunday),
        };

        /// <summary>The bits that are ticked, as the one number the rule stores.</summary>
        public int DaysMask() => this.Days.Aggregate(0, (mask, bit) => mask | bit) & TimeConditionRule.AllDays;

        /// <summary>Whether this row is empty enough to be a row the admin added and left alone.</summary>
        public bool IsBlank() =>
            this.Days.Count == 0 &&
            string.IsNullOrWhiteSpace(this.StartTime) &&
            string.IsNullOrWhiteSpace(this.EndTime);

        public bool IsTicked(int bit) => (this.DaysMask() & bit) != 0;
    }
}
