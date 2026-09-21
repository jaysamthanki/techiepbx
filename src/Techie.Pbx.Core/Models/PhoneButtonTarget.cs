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
        /// <summary>
        /// A lamp on somebody else's extension, and press to dial them: shows whether that phone
        /// is free, ringing or busy. Called 'Extension' until schema 020 (D121).
        /// </summary>
        public const string Blf = "Blf";

        /// <summary>
        /// The extension <em>this</em> phone registers as. Every phone has at least one, and they
        /// are the leading keys, because that is where a handset puts its own line appearances.
        /// </summary>
        public const string Line = "Line";

        /// <summary>A key nobody has assigned. Stored as no row at all, never as this string.</summary>
        public const string None = "";

        /// <summary>A parking slot: the lamp is lit while a call is parked there, press to take it.</summary>
        public const string ParkingSlot = "ParkingSlot";

        private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { Blf, Line, ParkingSlot };

        /// <summary>
        /// Whether this kind names an extension number. Both do: a line is the extension the phone
        /// signs in as and a BLF is one it watches, so everything that asks "does that extension
        /// still exist?" asks it of both.
        /// </summary>
        public static bool IsExtension(string targetType) =>
            string.Equals(targetType, Blf, StringComparison.Ordinal) ||
            string.Equals(targetType, Line, StringComparison.Ordinal);

        public static bool IsKnown(string targetType) => Known.Contains(targetType);
    }
}
