using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Core.Reports;

namespace Techie.Pbx.Asterisk.Status
{
    /// <summary>
    /// Turns the channels Asterisk has open into the calls a person would count. A pure function
    /// over an already-read list, like the renderers and <see cref="Ami.RegistrationStatus.Map"/>:
    /// no connection, so what it does to a given set of channels can be pinned down in tests.
    ///
    /// The rule is the linked id, and failing that the bridge. Every channel of one call shares the
    /// Linkedid — the caller, the phones still ringing, the leg out to a trunk — so a call being
    /// dialled is one row and not one per channel. The side that started it is the channel whose
    /// Uniqueid is that Linkedid; without one, the oldest, since Asterisk creates the caller's
    /// channel first and the leg it dials out on second (D35's reasoning, applied to live channels).
    ///
    /// The ends are named the way the call reports name them (<see cref="CdrParties"/>): an
    /// extension of ours is its number and name, read from its channel, so an outbound call reads
    /// as the phone that made it and not as the caller ID it went out with. That caller ID is the
    /// row's <see cref="ActiveCall.Line"/>.
    /// </summary>
    public static class ActiveCalls
    {
        /// <summary>
        /// What Asterisk puts in a caller ID header it has nothing for. Shown as nothing at all,
        /// because "&lt;unknown&gt;" in a table column is worse than a blank one.
        /// </summary>
        private const string Unknown = "<unknown>";

        /// <summary>One row per call, longest first.</summary>
        public static List<ActiveCall> FromChannels(IEnumerable<ActiveChannel> channels, PbxEndpoints endpoints)
        {
            var calls = new List<ActiveCall>();

            foreach (var group in Groups(channels))
            {
                var from = group.FirstOrDefault(channel => channel.UniqueID.Length > 0 && channel.UniqueID == channel.LinkedID)
                    ?? group.OrderByDescending(channel => channel.DurationSpan ?? TimeSpan.Zero).First();

                var others = group.Where(channel => channel != from).Select(channel => CdrChannels.Endpoint(channel.Channel)).ToList();
                var fromEndpoint = CdrChannels.Endpoint(from.Channel);
                var fromExtension = endpoints.IsExtension(fromEndpoint) ? fromEndpoint : null;

                // Inbound when the caller is a trunk, outbound when the call reaches one.
                var trunk = endpoints.IsTrunk(fromEndpoint) ? fromEndpoint : others.FirstOrDefault(endpoints.IsTrunk);
                var outbound = trunk != null && !endpoints.IsTrunk(fromEndpoint);

                calls.Add(new ActiveCall
                {
                    Channels = group.Select(channel => channel.Channel).ToList(),
                    Duration = from.DurationSpan,
                    From = fromExtension != null ? endpoints.Label(fromExtension) : Caller(from),
                    Line = outbound ? Known(from.CallerIDNum) : "",
                    State = from.ChannelStateDesc,
                    To = Called(from, others, endpoints),
                    Trunk = trunk,
                });
            }

            return calls
                .OrderByDescending(call => call.Duration ?? TimeSpan.Zero)
                .ToList();
        }

        /// <summary>
        /// Who the calling side reached: the extensions of ours on the call, every one still
        /// ringing included. When there are none, the connected line, which Asterisk fills in once
        /// it knows; before that, the number being dialled is the best answer there is.
        /// </summary>
        private static string Called(ActiveChannel from, List<string?> others, PbxEndpoints endpoints)
        {
            var extensions = others
                .Where(endpoints.IsExtension)
                .Select(endpoint => endpoint!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (extensions.Count > 0)
                return string.Join(", ", extensions.Select(endpoints.Label));

            var connected = Known(from.ConnectedLineNum);

            return connected.Length > 0 ? connected : Known(from.Exten);
        }

        /// <summary>The caller as one label: "2025551234 (Alice Smith)", or whichever half exists.</summary>
        private static string Caller(ActiveChannel channel)
        {
            var name = Known(channel.CallerIDName);
            var number = Known(channel.CallerIDNum);

            if (name.Length == 0)
                return number;

            return number.Length == 0 ? name : $"{number} ({name})";
        }

        /// <summary>
        /// The channels of each call: everything sharing a Linkedid is a group. A channel Asterisk
        /// sent no Linkedid for falls back to its bridge, and one in no bridge either is a group of
        /// its own. Groups keep the order their first channel arrived in, so the answer is stable.
        /// </summary>
        private static List<List<ActiveChannel>> Groups(IEnumerable<ActiveChannel> channels)
        {
            var keyed = new Dictionary<string, List<ActiveChannel>>(StringComparer.Ordinal);
            var groups = new List<List<ActiveChannel>>();

            foreach (var channel in channels)
            {
                var key = channel.LinkedID.Length > 0 ? "linked:" + channel.LinkedID
                    : channel.BridgeId.Length > 0 ? "bridge:" + channel.BridgeId
                    : null;

                if (key == null)
                {
                    groups.Add(new List<ActiveChannel> { channel });
                    continue;
                }

                if (!keyed.TryGetValue(key, out var group))
                {
                    group = new List<ActiveChannel>();
                    keyed[key] = group;
                    groups.Add(group);
                }

                group.Add(channel);
            }

            return groups;
        }

        /// <summary>A header Asterisk had no value for, read as the empty string it means.</summary>
        private static string Known(string header) =>
            string.Equals(header, Unknown, StringComparison.OrdinalIgnoreCase) ? "" : header;
    }
}
