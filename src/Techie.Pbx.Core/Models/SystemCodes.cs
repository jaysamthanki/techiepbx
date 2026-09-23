namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// The star codes the system itself dials on, spelled once. The renderers that write them
    /// (<c>ExtensionsConfRenderer</c>, <c>FeaturesConfRenderer</c>) take their constants from here,
    /// and a call flow control's feature code is checked against the same list before it is saved,
    /// so a code an admin picks can never shadow one the dialplan already answers.
    ///
    /// In Core rather than next to the renderers because the check that needs them is a
    /// repository's, and Core cannot see the Asterisk project.
    /// </summary>
    public static class SystemCodes
    {
        /// <summary>The attended transfer, during a call (D119 additions). <c>features.conf</c>'s atxfer.</summary>
        public const string AttendedTransfer = "*2";

        /// <summary>Hear your own voice played back.</summary>
        public const string EchoTest = "*43";

        /// <summary>
        /// Directed pickup: this, then the number of the extension that is ringing (D120). A prefix
        /// rather than a whole code, so every code that starts with it is taken — <c>*8</c> followed
        /// by a two-digit extension is three digits after the star.
        /// </summary>
        public const string PickupPrefix = "*8";

        /// <summary>Listen to your own voicemail.</summary>
        public const string VoicemailMain = "*97";

        /// <summary>
        /// Why a star code would clash with one the system already dials on, or null when it does
        /// not. <paramref name="parkCode"/> is the configured park code, which is a setting rather
        /// than a constant (D119); it is a code pressed during a call rather than dialled, but one
        /// code meaning two things is a cheat sheet nobody can follow.
        /// </summary>
        public static string? Clash(string code, string parkCode)
        {
            if (string.Equals(code, EchoTest, StringComparison.Ordinal))
                return $"{code} is the echo test.";

            if (string.Equals(code, VoicemailMain, StringComparison.Ordinal))
                return $"{code} is how a user checks their voicemail.";

            if (string.Equals(code, AttendedTransfer, StringComparison.Ordinal))
                return $"{code} is the announced transfer.";

            if (code.StartsWith(PickupPrefix, StringComparison.Ordinal))
                return $"Codes starting {PickupPrefix} are taken by call pickup: {PickupPrefix} followed by a ringing extension's number.";

            if (string.Equals(code, parkCode, StringComparison.Ordinal))
                return $"{code} is the code that parks a call.";

            return null;
        }
    }
}
