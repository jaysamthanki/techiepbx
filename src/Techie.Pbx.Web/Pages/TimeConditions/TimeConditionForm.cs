using Techie.Pbx.Web.Pages.Shared;

namespace Techie.Pbx.Web.Pages.TimeConditions
{
    /// <summary>
    /// What the create/edit form in the modal shows, and what it posts back. One form is the whole
    /// condition — three destinations, the open hours and the holidays together (D62) — because
    /// that is how an admin thinks about it and because a save replaces the rules wholesale.
    ///
    /// The text fields are nullable because model binding turns a field the user left blank into
    /// null whatever the initialiser says.
    /// </summary>
    public class TimeConditionForm
    {
        /// <summary>Where a call outside the open hours goes, as one string (D35).</summary>
        public string? ClosedDestination { get; set; } = "";

        /// <summary>The picker for it, filled in by the page. Not posted back.</summary>
        public DestinationSelect ClosedDestinationChoices { get; set; } = new();

        public string? Description { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public List<string> Errors { get; set; } = new();

        /// <summary>Where a call on a holiday goes, unless that date carries its own (D63).</summary>
        public string? HolidayDestination { get; set; } = "";

        /// <summary>The picker for it, filled in by the page. Not posted back.</summary>
        public DestinationSelect HolidayDestinationChoices { get; set; } = new();

        /// <summary>The holiday dates, in the order the form lists them. Posted back whole.</summary>
        public List<TimeConditionHolidayForm> Holidays { get; set; } = new();

        /// <summary>
        /// The blank holiday row the Add button copies, carrying the placeholder key the page's
        /// JavaScript swaps for a real one. Rendered inside a &lt;template&gt;, so it is never posted.
        /// </summary>
        public TimeConditionHolidayForm HolidayTemplate { get; set; } = new();

        /// <summary>The open windows, in the order the form lists them. Posted back whole.</summary>
        public List<TimeConditionHoursForm> Hours { get; set; } = new();

        /// <summary>The blank open-hours row the Add button copies. Never posted, like the other.</summary>
        public TimeConditionHoursForm HoursTemplate { get; set; } = new();

        public bool IsNew => this.TimeConditionID == 0;
        public string? Name { get; set; } = "";

        /// <summary>Where a call inside the open hours goes, as one string (D35).</summary>
        public string? OpenDestination { get; set; } = "";

        /// <summary>The picker for it, filled in by the page. Not posted back.</summary>
        public DestinationSelect OpenDestinationChoices { get; set; } = new();

        public string? PlayExtension { get; set; } = "";
        public long TimeConditionID { get; set; }
    }
}
