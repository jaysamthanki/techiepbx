namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// The vendor a phone row belongs to (piece 22c). Every phone the Polycom controller
    /// auto-adds is <see cref="Polycom"/>; the Yealink controller writes <see cref="Yealink"/>.
    /// Kept as plain strings, the way <c>SettingsKeys</c> keeps its keys, rather than an enum:
    /// the value is stored and compared as text, never as an ordinal.
    /// </summary>
    public static class PhoneBrand
    {
        public const string Polycom = "Polycom";
        public const string Yealink = "Yealink";

        private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { Polycom, Yealink };

        public static bool IsKnown(string brand) => Known.Contains(brand);
    }
}
