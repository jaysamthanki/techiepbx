using Techie.Pbx.Asterisk.Ami;

namespace Techie.Pbx.Asterisk.Status
{
    /// <summary>
    /// Turns the channels Asterisk has open into the calls a person would count. A pure function
    /// over an already-read list, like the renderers and <see cref="Ami.RegistrationStatus.Map"/>:
    /// no connection, so what it does to a given set of channels can be pinned down in tests.
    ///
    /// The rule is the bridge. Two channels in the same bridge are one call, and the older of the
    /// two is the side that started it — Asterisk creates the caller's channel first and the leg
    /// it dials out on second, so "longest duration" is what tells the two apart without us having
    /// to know which direction the call went (D35's reasoning, applied to live channels).
    /// </summary>
    public static class ActiveCalls
    {
        /// <summary>
        /// What Asterisk puts in a caller ID header it has nothing for. Shown as nothing at all,
        /// because "&lt;unknown&gt;" in a table column is worse than a blank one.
        /// </summary>
        private const string Unknown = "<unknown>";

        /// <summary>
        /// One row per bridge, plus one for every channel that is not in a bridge yet, longest
        /// call first.
        /// </summary>
        public static List<ActiveCall> FromChannels(IEnumerable<ActiveChannel> channels)
        {
            var calls = new List<ActiveCall>();

            foreach (var group in Groups(channels))
            {
                var from = group.OrderByDescending(channel => channel.DurationSpan ?? TimeSpan.Zero).First();

                calls.Add(new ActiveCall
                {
                    Channels = group.Select(channel => channel.Channel).ToList(),
                    Duration = from.DurationSpan,
                    From = Caller(from),
                    State = from.ChannelStateDesc,
                    To = Called(from),
                });
            }

            return calls
                .OrderByDescending(call => call.Duration ?? TimeSpan.Zero)
                .ToList();
        }

        /// <summary>
        /// Who the calling side reached. The connected line is what Asterisk fills in once it
        /// knows; before that, the extension being dialled is the best answer there is.
        /// </summary>
        private static string Called(ActiveChannel channel)
        {
            var connected = Known(channel.ConnectedLineNum);

            return connected.Length > 0 ? connected : Known(channel.Exten);
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
        /// The channels of each call: every bridge is a group, and a channel with no bridge is a
        /// group of its own because it is a call being set up, not half of somebody else's.
        /// Bridges keep the order their first channel arrived in, so the answer is stable.
        /// </summary>
        private static List<List<ActiveChannel>> Groups(IEnumerable<ActiveChannel> channels)
        {
            var bridges = new Dictionary<string, List<ActiveChannel>>(StringComparer.Ordinal);
            var groups = new List<List<ActiveChannel>>();

            foreach (var channel in channels)
            {
                if (channel.BridgeId.Length == 0)
                {
                    groups.Add(new List<ActiveChannel> { channel });
                    continue;
                }

                if (!bridges.TryGetValue(channel.BridgeId, out var bridge))
                {
                    bridge = new List<ActiveChannel>();
                    bridges[channel.BridgeId] = bridge;
                    groups.Add(bridge);
                }

                bridge.Add(channel);
            }

            return groups;
        }

        /// <summary>A header Asterisk had no value for, read as the empty string it means.</summary>
        private static string Known(string header) =>
            string.Equals(header, Unknown, StringComparison.OrdinalIgnoreCase) ? "" : header;
    }
}
