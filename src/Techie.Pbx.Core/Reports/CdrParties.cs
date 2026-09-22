using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Reports
{
    /// <summary>
    /// Who a call record was between, as a person reads it rather than as Asterisk wrote it. Src
    /// and Dst are caller ID and dialled number, so an outbound call reads as the number it was
    /// presented as calling the number it dialled, and nobody can tell which phone made it. This
    /// puts the extensions back, and moves the number the call used on the trunk into a column of
    /// its own (<see cref="Line"/>).
    ///
    /// Worked out when a report is read, never stored, like the direction (F5).
    /// </summary>
    public class CdrParties
    {
        public CallDirection Direction { get; set; }

        /// <summary>The extension, labelled with its name, when the call came from one; else the caller's number.</summary>
        public string From { get; set; } = "";

        /// <summary>The extension number the call came from, or null when it came from outside.</summary>
        public string? FromExtension { get; set; }

        /// <summary>
        /// The number the call used on the trunk: the DID the caller dialled, for an inbound call,
        /// and the caller ID it went out with, for an outbound one. Null for an internal call. An
        /// inbound record from before the DID was collected has only Dst, which is the best there is.
        /// </summary>
        public string? Line { get; set; }

        /// <summary>The extension, labelled with its name, when the call went to one; else the dialled number.</summary>
        public string To { get; set; } = "";

        /// <summary>The extension number the call went to, or null when it went anywhere else.</summary>
        public string? ToExtension { get; set; }

        /// <summary>The trunk the record went over, or null for an internal one.</summary>
        public string? Trunk { get; set; }

        /// <summary>
        /// The parties to one record. <paramref name="origins"/> is the first leg of each call, by
        /// its UniqueID (<see cref="NeedsOrigin"/>): a record whose caller is a Local channel — the
        /// far half of a forwarded call (D130) — did not start there, and the call's first leg is
        /// what says who did. Every leg of a call shares the LinkedID, which is the UniqueID of the
        /// channel that started it.
        /// </summary>
        public static CdrParties For(Cdr cdr, PbxEndpoints endpoints, IReadOnlyDictionary<string, Cdr> origins)
        {
            var direction = CdrChannels.Direction(cdr, endpoints.TrunkNames);

            var caller = cdr;
            if (NeedsOrigin(cdr) && origins.TryGetValue(cdr.LinkedID!, out var origin))
                caller = origin;

            var fromEndpoint = CdrChannels.Endpoint(caller.Channel);
            var fromExtension = endpoints.IsExtension(fromEndpoint) ? fromEndpoint : null;

            var toEndpoint = CdrChannels.Endpoint(cdr.DestinationChannel);
            var toExtension = endpoints.IsExtension(toEndpoint) ? toEndpoint : null;

            return new CdrParties
            {
                Direction = direction,
                From = fromExtension != null ? endpoints.Label(fromExtension) : caller.Src,
                FromExtension = fromExtension,
                Line = direction switch
                {
                    CallDirection.Inbound => cdr.Did ?? NullIfEmpty(cdr.Dst),
                    CallDirection.Outbound => NullIfEmpty(cdr.Src),
                    _ => null,
                },
                To = toExtension != null ? endpoints.Label(toExtension) : cdr.Dst,
                ToExtension = toExtension,
                Trunk = CdrChannels.Trunk(cdr, endpoints.TrunkNames),
            };
        }

        /// <summary>
        /// Whether a record's caller is no endpoint of ours — a Local channel — so who made the call
        /// has to be read from the call's first leg, the one whose UniqueID is this LinkedID.
        /// </summary>
        public static bool NeedsOrigin(Cdr cdr) =>
            CdrChannels.Endpoint(cdr.Channel) == null
            && !string.IsNullOrEmpty(cdr.LinkedID)
            && !string.Equals(cdr.LinkedID, cdr.UniqueID, StringComparison.Ordinal);

        private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
    }
}
