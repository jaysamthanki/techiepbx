using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.TimeConditions
{
    /// <summary>
    /// A destination as one cell of the table. The other list pages show one destination per row
    /// and can spell it out, but a time condition has three, so this table shows the short form —
    /// "IVR 7102", "Ext 1001" — and keeps the catalog's own wording as the cell's tooltip.
    /// </summary>
    public class DestinationLabel
    {
        /// <summary>What the catalog calls it, e.g. "7102 Main menu". The tooltip.</summary>
        public string Full { get; set; } = "";

        /// <summary>The kind and what it points at, e.g. "IVR 7102". What the cell reads.</summary>
        public string Short { get; set; } = "";

        /// <summary>
        /// Whether it is still somewhere a call can go. When it is not, the row says so: a call
        /// sent there is hung up until an admin fixes it.
        /// </summary>
        public bool Usable { get; set; }

        /// <summary>
        /// One destination, described both ways. The catalog is what decides whether it is still
        /// there (D35); the short form is built from the destination itself, so it reads the same
        /// whether the target is still there or not.
        /// </summary>
        public static DestinationLabel For(List<DestinationChoice> choices, Destination destination)
        {
            var choice = choices.FirstOrDefault(c =>
                string.Equals(c.Destination.Key, destination.Key, StringComparison.Ordinal));

            return new DestinationLabel
            {
                Full = choice?.Label ?? $"{destination.Key} — not there any more",
                Short = Describe(destination),
                Usable = choice != null,
            };
        }

        /// <summary>The short form: a word for the kind, and the number it points at.</summary>
        private static string Describe(Destination destination) => destination.Type switch
        {
            DestinationType.Announcement => $"Announcement {destination.Value}",
            DestinationType.Extension => $"Ext {destination.Value}",
            DestinationType.Hangup => "Hang up",
            DestinationType.Ivr => $"IVR {destination.Value}",
            DestinationType.RingGroup => $"Group {destination.Value}",
            DestinationType.TimeCondition => $"Time condition {destination.Value}",
            DestinationType.Voicemail => $"Voicemail {destination.Value}",
            _ => destination.Key,
        };
    }
}
