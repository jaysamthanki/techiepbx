namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// One channel Asterisk has open right now, from a CoreShowChannel event. A call is usually
    /// two of these bridged together, so this is half of what the status page shows as a call.
    ///
    /// Everything is taken as Asterisk sends it, including <see cref="Duration"/>'s "HH:MM:SS":
    /// turning the pile of channels into calls is somebody else's job (ActiveCalls), and this
    /// class only has to be a faithful reading of the event.
    /// </summary>
    public class ActiveChannel
    {
        /// <summary>The dialplan application the channel is in, e.g. "Dial" or "Playback".</summary>
        public string Application { get; set; } = "";

        public string ApplicationData { get; set; } = "";

        /// <summary>
        /// The bridge this channel is in, or empty when it is in none. Two channels sharing one
        /// bridge id are the two ends of the same call; a channel with no bridge is still ringing
        /// or dialling.
        /// </summary>
        public string BridgeId { get; set; } = "";

        public string CallerIDName { get; set; } = "";
        public string CallerIDNum { get; set; } = "";

        /// <summary>The channel name, e.g. "PJSIP/1001-00000012".</summary>
        public string Channel { get; set; } = "";

        /// <summary>Asterisk's own word for the state: "Up", "Ring", "Ringing", "Down".</summary>
        public string ChannelStateDesc { get; set; } = "";

        public string ConnectedLineName { get; set; } = "";
        public string ConnectedLineNum { get; set; } = "";

        public string Context { get; set; } = "";

        /// <summary>How long the channel has existed, as Asterisk writes it: "HH:MM:SS".</summary>
        public string Duration { get; set; } = "";

        public string Exten { get; set; } = "";

        /// <summary>
        /// The call this channel is part of: the Uniqueid of the channel that started it, shared by
        /// every channel the call has — the phones still ringing, the leg out to a trunk, both
        /// halves of a Local channel — whether or not they are bridged yet.
        /// </summary>
        public string LinkedID { get; set; } = "";

        /// <summary>This channel's own id. The channel whose Uniqueid is the LinkedID started the call.</summary>
        public string UniqueID { get; set; } = "";

        /// <summary>
        /// <see cref="Duration"/> as a span, or null when Asterisk sent something we cannot read.
        /// Null rather than zero: "we do not know how long" and "it just started" are different
        /// answers, and a sort would put them in different places.
        /// </summary>
        public TimeSpan? DurationSpan =>
            TimeSpan.TryParse(this.Duration, out var span) ? span : null;

        public static ActiveChannel FromEvent(AmiMessage message) => new()
        {
            Application = message.Get("Application") ?? "",
            ApplicationData = message.Get("ApplicationData") ?? "",
            BridgeId = message.Get("BridgeId") ?? "",
            CallerIDName = message.Get("CallerIDName") ?? "",
            CallerIDNum = message.Get("CallerIDNum") ?? "",
            Channel = message.Get("Channel") ?? "",
            ChannelStateDesc = message.Get("ChannelStateDesc") ?? "",
            ConnectedLineName = message.Get("ConnectedLineName") ?? "",
            ConnectedLineNum = message.Get("ConnectedLineNum") ?? "",
            Context = message.Get("Context") ?? "",
            Duration = message.Get("Duration") ?? "",
            Exten = message.Get("Exten") ?? "",
            LinkedID = message.Get("Linkedid") ?? "",
            UniqueID = message.Get("Uniqueid") ?? "",
        };
    }
}
