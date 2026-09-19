using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Asterisk.Status;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Turning channels into calls: which two are one call, which end started it, and what the
    /// row ends up saying. All pure, so every case is a handful of channels made up here.
    /// </summary>
    public class ActiveCallsTests
    {
        private const string Bridge = "9c1f6d2a-0f11-4a2b-9a3f-6b0f5f1d2c34";

        private static ActiveChannel Channel(
            string channel,
            string duration,
            string bridgeId = "",
            string callerNum = "",
            string callerName = "",
            string connectedNum = "",
            string exten = "",
            string state = "Up") => new()
            {
                BridgeId = bridgeId,
                CallerIDName = callerName,
                CallerIDNum = callerNum,
                Channel = channel,
                ChannelStateDesc = state,
                ConnectedLineNum = connectedNum,
                Duration = duration,
                Exten = exten,
            };

        [Fact]
        public void No_channels_is_no_calls()
        {
            Assert.Empty(ActiveCalls.FromChannels(new List<ActiveChannel>()));
        }

        [Fact]
        public void Two_channels_in_one_bridge_are_one_call()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/1001-00000012", "00:01:17", Bridge, callerNum: "1001", connectedNum: "2025551234"),
                Channel("PJSIP/callcentric-00000013", "00:01:15", Bridge, callerNum: "2025551234", connectedNum: "1001"),
            });

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
            });

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
            });

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
            });

            Assert.Equal("1002", Assert.Single(calls).To);
        }

        [Fact]
        public void A_caller_name_is_shown_beside_the_number()
        {
            var calls = ActiveCalls.FromChannels(new List<ActiveChannel>
            {
                Channel("PJSIP/trunk-00000012", "00:00:30", callerNum: "2025551234", callerName: "Alice Smith"),
            });

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
            });

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
            });

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
            });

            Assert.Null(Assert.Single(calls).Duration);
        }
    }
}
