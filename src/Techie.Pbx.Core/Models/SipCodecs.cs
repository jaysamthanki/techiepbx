namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// The codecs this system is allowed to offer, in one place. The list is short on purpose: it
    /// is exactly the codec modules the modules.conf allowlist loads (D31), so naming anything
    /// else would generate a pjsip.conf Asterisk cannot honour (D73). Trunks and extensions both
    /// choose from it.
    /// </summary>
    public static class SipCodecs
    {
        /// <summary>What a trunk or an extension is offered when nobody has chosen.</summary>
        public const string Default = "ulaw,alaw";

        /// <summary>
        /// ulaw and alaw are the two G.711 flavours every provider speaks; gsm is here because
        /// Asterisk's own prompts ship in it; g722 is the wideband HD voice the desk phones
        /// speak; slin is signed linear, offered where a device uses it natively (D117).
        /// Adding to this list means adding the codec's module to the allowlist first.
        /// </summary>
        public static readonly IReadOnlyList<string> Allowed = new[] { "ulaw", "alaw", "gsm", "g722", "slin" };

        public static bool IsAllowed(string codec) => Allowed.Contains(codec, StringComparer.Ordinal);

        /// <summary>A comma separated list as its entries, in order, ignoring blanks.</summary>
        public static List<string> Parse(string? value) =>
            (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
