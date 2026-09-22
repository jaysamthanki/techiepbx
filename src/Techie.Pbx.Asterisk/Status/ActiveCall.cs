namespace Techie.Pbx.Asterisk.Status
{
    /// <summary>
    /// One call in progress, as the status page shows it: the two ends of a bridge read as a
    /// single row, or a channel that has not been bridged yet read on its own.
    ///
    /// Deliberately small. This is "who is talking to whom, and for how long" — not a call record:
    /// nothing here is stored, and what a call did is a question for call history (F5), not for a
    /// page that redraws every five seconds.
    /// </summary>
    public class ActiveCall
    {
        /// <summary>
        /// The channel names behind this row, for a tooltip. Two for a bridged call, one for a
        /// call still being set up.
        /// </summary>
        public List<string> Channels { get; set; } = new();

        /// <summary>How long the calling side has existed, or null when Asterisk did not say.</summary>
        public TimeSpan? Duration { get; set; }

        /// <summary>
        /// Who is calling: the extension with its name when it is one of ours, else the caller's
        /// number with the name they sent after it.
        /// </summary>
        public string From { get; set; } = "";

        /// <summary>
        /// The caller ID an outbound call went out on the trunk as, which is which of the site's
        /// numbers the far end sees. Empty for any other call: the DID an inbound call was sent
        /// to is in the dialplan, not in anything the channel list says.
        /// </summary>
        public string Line { get; set; } = "";

        /// <summary>Asterisk's own word for the state of the calling side: "Up", "Ring", "Ringing".</summary>
        public string State { get; set; } = "";

        /// <summary>
        /// Who they are calling: the extensions of ours on the call with their names, else the
        /// connected line if there is one, else the number dialled.
        /// </summary>
        public string To { get; set; } = "";

        /// <summary>The trunk the call is on, or null when it is between extensions.</summary>
        public string? Trunk { get; set; }
    }
}
