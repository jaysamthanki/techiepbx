using Techie.Pbx.Web.Pages.Shared;

namespace Techie.Pbx.Web.Pages.TimeConditions
{
    /// <summary>
    /// One holiday row of the form: the date the site is closed on, and where that one day sends a
    /// call when it should not be the condition's holiday destination (D63). Indexed by
    /// <see cref="Key"/> for the reason an open-hours row is.
    /// </summary>
    public class TimeConditionHolidayForm
    {
        /// <summary>The picker for the override, filled in by the page. Not posted back.</summary>
        public DestinationSelect Choices { get; set; } = new();

        /// <summary>The date, "YYYY-MM-DD", which is what a date input posts.</summary>
        public string? Date { get; set; } = "";

        /// <summary>The override as the picker posts it. Empty means the condition's own.</summary>
        public string? Destination { get; set; } = "";

        /// <summary>What this row's field names are indexed by. Set by the page.</summary>
        public string Key { get; set; } = "";

        /// <summary>Whether this row is empty enough to be a row the admin added and left alone.</summary>
        public bool IsBlank() => string.IsNullOrWhiteSpace(this.Date) && string.IsNullOrWhiteSpace(this.Destination);
    }
}
