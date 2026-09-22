using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Reports
{
    /// <summary>
    /// What a call record's channel names say about it: which endpoint each side was, and so which
    /// way the call went. The one place direction is worked out, and it is worked out when a report
    /// is read, never stored, so a report follows the trunks as they are now (F5).
    ///
    /// A PJSIP channel is named "PJSIP/&lt;endpoint&gt;-&lt;counter&gt;", and in our generated
    /// pjsip.conf the endpoint is a trunk's Name or an extension's Number. The two cannot be
    /// confused: a trunk name starts with a letter and an extension number is digits.
    /// </summary>
    public static class CdrChannels
    {
        private const string PjsipPrefix = "PJSIP/";

        /// <summary>
        /// Inbound when the caller's channel is a trunk, outbound when the called channel is,
        /// internal when neither is. A record with a trunk on both sides counts as inbound, since
        /// that is where it came from.
        /// </summary>
        public static CallDirection Direction(Cdr cdr, IReadOnlySet<string> trunkNames)
        {
            if (IsTrunk(Endpoint(cdr.Channel), trunkNames))
                return CallDirection.Inbound;

            if (IsTrunk(Endpoint(cdr.DestinationChannel), trunkNames))
                return CallDirection.Outbound;

            return CallDirection.Internal;
        }

        /// <summary>
        /// The endpoint a PJSIP channel belongs to: "PJSIP/101-0000001a" is "101",
        /// "PJSIP/voip-ms-0000001b" is "voip-ms". Null for anything that is not a PJSIP channel —
        /// a Local channel, an empty destination — because it is no endpoint of ours.
        /// </summary>
        public static string? Endpoint(string? channel)
        {
            if (string.IsNullOrEmpty(channel) || !channel.StartsWith(PjsipPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var rest = channel[PjsipPrefix.Length..];

            // The counter is after the last dash; an endpoint name can have dashes of its own.
            var dash = rest.LastIndexOf('-');
            var endpoint = dash > 0 ? rest[..dash] : rest;

            return endpoint.Length == 0 ? null : endpoint;
        }

        /// <summary>
        /// The trunk a record went over, or null for an internal one: the caller's side for an
        /// inbound call, the called side for an outbound one.
        /// </summary>
        public static string? Trunk(Cdr cdr, IReadOnlySet<string> trunkNames) =>
            Direction(cdr, trunkNames) switch
            {
                CallDirection.Inbound => Endpoint(cdr.Channel),
                CallDirection.Outbound => Endpoint(cdr.DestinationChannel),
                _ => null,
            };

        private static bool IsTrunk(string? endpoint, IReadOnlySet<string> trunkNames) =>
            endpoint != null && trunkNames.Contains(endpoint);
    }
}
