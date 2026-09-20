namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// What a parked caller hears while they wait (D119). A fixed list rather than free text, so
    /// the settings form offers it as a dropdown (D75) and nothing can be stored that the
    /// generated config could not honour.
    ///
    /// Silence is first, and so the default: a parked call that plays nothing is quiet but
    /// unmistakably still connected, and music is a file somebody has to upload before it can be
    /// anything other than silence anyway.
    /// </summary>
    public static class ParkingAudio
    {
        /// <summary>
        /// The uploaded music on hold tracks, as the one generated class. Asterisk decides the
        /// class at park time, so choosing this with no tracks uploaded is still silence.
        /// </summary>
        public const string MusicOnHold = "moh";

        /// <summary>Nothing at all: Asterisk holds the channel open and sends comfort silence.</summary>
        public const string Silence = "silence";

        /// <summary>Both, in the order the settings form offers them. The first is the default.</summary>
        public static IReadOnlyList<string> All { get; } = new[] { Silence, MusicOnHold };

        /// <summary>Whether this is one of the two words the setting may be stored as.</summary>
        public static bool IsKnown(string value) => All.Contains(value.Trim(), StringComparer.Ordinal);

        /// <summary>Whether this value means music rather than silence.</summary>
        public static bool IsMusicOnHold(string value) =>
            string.Equals(value.Trim(), MusicOnHold, StringComparison.Ordinal);
    }
}
