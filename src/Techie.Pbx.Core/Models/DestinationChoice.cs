namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// One line of the destination picker: where the call would go, what to call it on screen,
    /// and which heading it sits under.
    /// </summary>
    public class DestinationChoice
    {
        public Destination Destination { get; set; } = new();

        /// <summary>The optgroup heading, e.g. "Extensions".</summary>
        public string GroupName { get; set; } = "";

        /// <summary>What the admin reads, e.g. "1001 Front Desk".</summary>
        public string Label { get; set; } = "";
    }
}
