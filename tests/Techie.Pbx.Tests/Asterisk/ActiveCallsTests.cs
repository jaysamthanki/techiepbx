using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Asterisk.Status;
using Techie.Pbx.Core.Reports;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Turning channels into calls: which two are one call, which end started it, and what the
    /// row ends up saying. All pure, so every case is a handful of channels made up here.
    /// "callcentric" and "trunk" are trunks; 100 is Front Desk and 1002 is Sales, and the other
    /// extensions have no name.
    /// </summary>
    public class ActiveCallsTests
    {
        private const string Bridge = "9c1f6d2a-0f11-4a2b-9a3f-6b0f5f1d2c34";

        private static readonly PbxEndpoints Endpoints = new(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["100"] = "Front Desk", ["1002"] = "Sales" },
            new HashSet<string>(StringComparer.Ordinal) { "callcentric", "trunk" });

        private static ActiveChannel Channel(
            string channel,
            string duration,
            string bridgeId = "",
            string callerNum = "",
            string callerName = "",
            string connectedNum = "",
            string exten = "",
            string state = "Up",
            string uniqueID = "",
            string linkedID = "") => new()
            {
                BridgeId = bridgeId,
                CallerIDName = callerName,
                CallerIDNum = callerNum,
                Channel = channel,
                ChannelStateDesc = state,
                ConnectedLineNum = connectedNum,
                Duration = duration,
                Exten = exten,
                LinkedID = linkedID,
                UniqueID = uniqueID,
            };

        [Fact]
        public void No_channels_is_no_calls()
        {
            Assert.Empty(ActiveCalls.FromChannels(new List<ActiveChannel>(), Endpoints));
        }

        [Fact]
        public void Two_channels_in_one_bridge_are_one_call()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/1001-00000012", "00:01:17", Bridge, callerNum: "1001", connectedNum: "2025551234"),
                Channel("PJSIP/callcentric-00000013", "00:01:15", Bridge, callerNum: "2025551234", connectedNum: "1001"),
            }, Endpoints);

            var call = Assert.Single(calls);
            Assert.Equal("1001", call.From);
            Assert.Equal("2025551234", call.To);
            Assert.Equal(TimeSpan.FromSeconds(77), call.Duration);
            Assert.Equal(new[] { "PJSIP/1001-00000012", "PJSIP/callcentric-00000013" }, call.Channels);
        }

        /// <summary>
        /// Asterisk makes the caller's channel first, so the older end is the one that dialled.
        /// Whichever order the events arrived in has to give the same answer.
        /// </summary>
        [Fact]
        public void The_older_channel_in_a_bridge_is_the_calling_side()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/callcentric-00000013", "00:00:04", Bridge, callerNum: "2025551234", connectedNum: "1001"),
                Channel("PJSIP/1001-00000012", "00:00:09", Bridge, callerNum: "1001", connectedNum: "2025551234"),
            }, Endpoints);

            var call = Assert.Single(calls);
            Assert.Equal("1001", call.From);
            Assert.Equal("2025551234", call.To);
        }

        [Fact]
        public void A_channel_with_no_bridge_is_a_call_of_its_own()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/1001-00000012", "00:00:03", exten: "1002", state: "Ring"),
                Channel("PJSIP/1003-00000014", "00:00:02", exten: "600", state: "Ringing"),
            }, Endpoints);

            Assert.Equal(2, calls.Count);
            Assert.Equal("Ring", calls[0].State);
            Assert.Equal("1002", calls[0].To);
            Assert.Equal("600", calls[1].To);
        }

        [Fact]
        public void The_connected_line_is_preferred_over_the_extension_being_dialled()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/1001-00000012", "00:00:30", connectedNum: "1002", exten: "600"),
            }, Endpoints);

            Assert.Equal("1002", Assert.Single(calls).To);
        }

        [Fact]
        public void A_caller_name_is_shown_beside_the_number()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/trunk-00000012", "00:00:30", callerNum: "2025551234", callerName: "Alice Smith"),
            }, Endpoints);

            Assert.Equal("2025551234 (Alice Smith)", Assert.Single(calls).From);
        }

        /// <summary>
        /// "&lt;unknown&gt;" is Asterisk saying it has nothing, and printing it in a table column
        /// would be printing the absence of an answer as if it were one.
        /// </summary>
        [Fact]
        public void Unknown_is_not_treated_as_a_caller_id()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/trunk-00000012", "00:00:30",
                    callerNum: "2025551234", callerName: "<unknown>", connectedNum: "<unknown>", exten: "1001"),
            }, Endpoints);

            var call = Assert.Single(calls);
            Assert.Equal("2025551234", call.From);
            Assert.Equal("1001", call.To);
        }

        [Fact]
        public void Calls_are_listed_longest_first()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/1001-00000012", "00:00:09", exten: "1002"),
                Channel("PJSIP/1003-00000014", "01:02:03", exten: "1004"),
                Channel("PJSIP/1005-00000016", "00:04:00", exten: "1006"),
            }, Endpoints);

            Assert.Equal(
                new[] { "1004", "1006", "1002" },
                calls.Select(call => call.To));
        }

        [Fact]
        public void A_duration_asterisk_did_not_send_is_no_duration_rather_than_zero()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/1001-00000012", "", exten: "1002"),
            }, Endpoints);

            Assert.Null(Assert.Single(calls).Duration);
        }

        /// <summary>
        /// The call the user reported (piece 18 follow-up), while it is still ringing: the phone
        /// and the leg out to the trunk are not bridged yet, but share the Linkedid, so they are one
        /// row. It reads as the extension that made it, and the caller ID it went out as is the line.
        /// </summary>
        [Fact]
        public void An_outbound_call_is_from_the_extension_with_the_caller_id_it_went_out_as()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/100-00000020", "00:00:06", callerNum: "7146085242", callerName: "Acme Sales",
                    exten: "19494411105", state: "Ring", uniqueID: "1758.20", linkedID: "1758.20"),
                Channel("PJSIP/callcentric-00000021", "00:00:05", callerNum: "19494411105", connectedNum: "7146085242",
                    state: "Ringing", uniqueID: "1758.21", linkedID: "1758.20"),
            }, Endpoints);

            var call = Assert.Single(calls);
            Assert.Equal("100 (Front Desk)", call.From);
            Assert.Equal("19494411105", call.To);
            Assert.Equal("7146085242", call.Line);
            Assert.Equal("callcentric", call.Trunk);
            Assert.Equal("Ring", call.State);
        }

        /// <summary>A call from outside ringing two phones is one row to both of them, and has no line.</summary>
        [Fact]
        public void An_inbound_call_ringing_two_phones_is_one_call_to_both_extensions()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/1001-00000031", "00:00:03", state: "Ringing", uniqueID: "1758.31", linkedID: "1758.30"),
                Channel("PJSIP/callcentric-00000030", "00:00:04", callerNum: "2025551234", callerName: "Alice Smith",
                    exten: "1001", state: "Ring", uniqueID: "1758.30", linkedID: "1758.30"),
                Channel("PJSIP/1002-00000032", "00:00:03", state: "Ringing", uniqueID: "1758.32", linkedID: "1758.30"),
            }, Endpoints);

            var call = Assert.Single(calls);
            Assert.Equal("2025551234 (Alice Smith)", call.From);
            Assert.Equal("1001, 1002 (Sales)", call.To);
            Assert.Equal("", call.Line);
            Assert.Equal("callcentric", call.Trunk);
        }

        [Fact]
        public void An_internal_call_is_between_extensions_by_number_and_name()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/100-00000040", "00:02:00", Bridge, callerNum: "100", callerName: "Reception", connectedNum: "1002",
                    uniqueID: "1758.40", linkedID: "1758.40"),
                Channel("PJSIP/1002-00000041", "00:01:58", Bridge, callerNum: "1002", connectedNum: "100",
                    uniqueID: "1758.41", linkedID: "1758.40"),
            }, Endpoints);

            var call = Assert.Single(calls);
            Assert.Equal("100 (Front Desk)", call.From);
            Assert.Equal("1002 (Sales)", call.To);
            Assert.Equal("", call.Line);
            Assert.Null(call.Trunk);
        }

        /// <summary>
        /// The channel that started the call is the one whose Uniqueid is the Linkedid, even when it
        /// is not the oldest, the way a call picked up from somewhere else can be.
        /// </summary>
        [Fact]
        public void The_channel_the_linkedid_names_is_the_calling_side()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/1001-00000051", "00:00:09", Bridge, uniqueID: "1758.51", linkedID: "1758.50"),
                Channel("PJSIP/100-00000050", "00:00:04", Bridge, uniqueID: "1758.50", linkedID: "1758.50"),
            }, Endpoints);

            var call = Assert.Single(calls);
            Assert.Equal("100 (Front Desk)", call.From);
            Assert.Equal("1001", call.To);
        }
    }
}
