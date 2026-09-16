namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// A registered PJSIP contact, from a ContactList event. This is how we know whether a phone
    /// is actually registered.
    /// </summary>
    public class PjsipContact
    {
        /// <summary>The AOR, which for our config is the extension number.</summary>
        public string Aor { get; set; } = "";

        public string Uri { get; set; } = "";

        /// <summary>Reachable, Unreachable, NonQualified, Unknown, Created or Removed.</summary>
        public string Status { get; set; } = "";

        public string UserAgent { get; set; } = "";

        public long RoundTripMicroseconds { get; set; }

        public static PjsipContact FromEvent(AmiMessage message) => new()
        {
            // ContactList carries no AOR header; Endpoint is the endpoint name, which in our
            // generated config is both the AOR name and the extension number (D19).
            Aor = message.Get("Endpoint") ?? "",
            Uri = message.Get("URI") ?? "",
            Status = message.Get("Status") ?? "",
            UserAgent = message.Get("UserAgent") ?? "",
            RoundTripMicroseconds = long.TryParse(message.Get("RoundtripUsec"), out var microseconds) ? microseconds : 0,
        };
    }
}
