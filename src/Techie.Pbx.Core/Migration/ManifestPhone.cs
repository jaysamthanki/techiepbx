namespace Techie.Pbx.Core.Migration
{
    /// <summary>One <c>phones[]</c> element of manifest v1 (docs/freepbx-import.md).</summary>
    public class ManifestPhone
    {
        public List<ManifestPhoneKey>? Keys { get; set; }
        public string? LastIP { get; set; }

        /// <summary>The extension the phone's line registers as, or null for a phone nobody was assigned.</summary>
        public string? Line { get; set; }

        public string? Mac { get; set; }
        public string? Model { get; set; }
    }
}
