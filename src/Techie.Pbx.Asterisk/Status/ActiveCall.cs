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

        /// <summary>Who is calling: the number, with the name after it when there is one.</summary>
        public string From { get; set; } = "";

        /// <summary>Asterisk's own word for the state of the calling side: "Up", "Ring", "Ringing".</summary>
        public string State { get; set; } = "";

        /// <summary>Who they are calling: the connected line if there is one, else the extension dialled.</summary>
        public string To { get; set; } = "";
    }
}
