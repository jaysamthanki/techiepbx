namespace Techie.Pbx.Core.Migration
{
    /// <summary>One key of a phone in manifest v1: a FreePBX Polycom attendant row.</summary>
    public class ManifestPhoneKey
    {
        public string? Label { get; set; }

        /// <summary>FreePBX's attendant position, 1-based, counted after the phone's line.</summary>
        public int Position { get; set; }

        /// <summary>The Polycom module's keyword, e.g. <c>blf</c> or <c>parking</c>.</summary>
        public string? Type { get; set; }

        public string? Value { get; set; }
    }
}
