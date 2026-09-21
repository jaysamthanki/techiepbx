namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// What an assignable key on a desk phone can be pointed at (D121). Kept as plain strings, the
    /// way <see cref="PhoneBrand"/> and <see cref="ParkingAudio"/> keep theirs: the value is stored
    /// and compared as text, never as an ordinal.
    ///
    /// Adding a kind is adding a constant here, teaching <see cref="PhoneButton.Validate"/> what it
    /// may point at and teaching the provisioning renderers how to write it. <b>Call flow control
    /// is the one this list is shaped for</b> — it is not built, and nothing here pretends it is,
    /// but it needs no schema change when it is (see 018_phone_buttons.sql).
    /// </summary>
    public static class PhoneButtonTarget
    {
        /// <summary>A BLF / quick-dial key for an extension: lamp, press to dial, long-press pickup.</summary>
        public const string Extension = "Extension";

        /// <summary>A key nobody has assigned. Stored as no row at all, never as this string.</summary>
        public const string None = "";

        /// <summary>A parking slot: the lamp is lit while a call is parked there, press to take it.</summary>
        public const string ParkingSlot = "ParkingSlot";

        private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { Extension, ParkingSlot };

        public static bool IsKnown(string targetType) => Known.Contains(targetType);
    }
}
