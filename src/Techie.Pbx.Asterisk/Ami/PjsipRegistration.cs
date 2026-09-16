namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// One outbound registration: us telling a provider where to send calls. This is how we know
    /// whether a trunk is up.
    /// </summary>
    public class PjsipRegistration
    {
        /// <summary>The URI we registered as, e.g. sip:17771234567@callcentric.com.</summary>
        public string ClientUri { get; set; } = "";

        /// <summary>
        /// The registration object's name, which in our generated config is the trunk name with
        /// "-reg" on the end (D39).
        /// </summary>
        public string ObjectName { get; set; } = "";

        /// <summary>The provider's URI.</summary>
        public string ServerUri { get; set; } = "";

        /// <summary>Registered, Unregistered, Rejected, Stopped — Asterisk's own words.</summary>
        public string Status { get; set; } = "";

        public static PjsipRegistration FromEvent(AmiMessage message) => new()
        {
            ClientUri = message.Get("ClientUri") ?? "",
            ObjectName = message.Get("ObjectName") ?? "",
            ServerUri = message.Get("ServerUri") ?? "",
            Status = message.Get("Status") ?? "",
        };
    }
}
