namespace Techie.Pbx.Web.Pages.TimeConditions
{
    /// <summary>One line of the time conditions table.</summary>
    public class TimeConditionRow
    {
        /// <summary>Where a call outside the open hours goes.</summary>
        public DestinationLabel ClosedDestination { get; set; } = new();

        public bool Enabled { get; set; }

        /// <summary>Where a call on a holiday goes, unless that date carries its own (D63).</summary>
        public DestinationLabel HolidayDestination { get; set; } = new();

        public string Name { get; set; } = "";

        /// <summary>Where a call inside the open hours goes.</summary>
        public DestinationLabel OpenDestination { get; set; } = new();

        /// <summary>The number to dial to run the check, or an em dash when there is none.</summary>
        public string PlayExtension { get; set; } = "";

        public long TimeConditionID { get; set; }
    }
}
