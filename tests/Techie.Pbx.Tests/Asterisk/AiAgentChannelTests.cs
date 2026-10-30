using Techie.Pbx.Asterisk.Ami;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Which channel an AI agent's call is: bound by the call's own UUID in the AudioSocket data
    /// (D208), with D203's find-by-number kept as the fallback for a session given no channel.
    /// </summary>
    public class AiAgentChannelTests
    {
        private const string Uuid = "8688d396-aeaa-48b6-abb5-645d5c58e0ec";

        private static ActiveChannel Channel(string name, string exten, string application = "AudioSocket", string context = "internal", string data = "") =>
            new() { Channel = name, Exten = exten, Application = application, Context = context, ApplicationData = data };

        [Fact]
        public void A_call_is_bound_by_its_uuid_and_the_channel_says_which_agent()
        {
            var channels = new[]
            {
                Channel("PJSIP/101-00000001", "7300", data: "1b4e28ba-2fa1-11d2-883f-0016d3cca427,127.0.0.1:9019"),
                Channel("PJSIP/callcentric-00000002", "7300", data: Uuid + ",127.0.0.1:9019"),
                Channel("PJSIP/102-00000003", "7400", data: "9b2c0a3e-1111-4c1d-8e3f-0016d3cca427,127.0.0.1:9019"),
            };

            var bound = AiAgentChannel.Bind(channels, Uuid);

            Assert.Equal("PJSIP/callcentric-00000002", bound?.Channel);
            Assert.Equal("7300", bound?.Exten);
        }

        [Fact]
        public void The_uuid_matches_whatever_case_asterisk_reports_it_in()
        {
            var channels = new[] { Channel("PJSIP/a-00000001", "7300", data: Uuid.ToUpperInvariant() + ",127.0.0.1:9019") };

            Assert.Equal("PJSIP/a-00000001", AiAgentChannel.Bind(channels, Uuid)?.Channel);
        }

        [Fact]
        public void An_audiosocket_channel_with_another_uuid_is_not_this_call()
        {
            var channels = new[] { Channel("PJSIP/a-00000001", "7300", data: "1b4e28ba-2fa1-11d2-883f-0016d3cca427,127.0.0.1:9019") };

            Assert.Null(AiAgentChannel.Bind(channels, Uuid));
        }

        /// <summary>Only AudioSocket's own data counts: the same text in another application is not the call.</summary>
        [Fact]
        public void A_channel_in_another_application_carrying_the_uuid_is_ignored()
        {
            var channels = new[]
            {
                Channel("PJSIP/a-00000001", "7300", application: "NoOp", data: Uuid + ",127.0.0.1:9019"),
                Channel("PJSIP/b-00000002", "7300", application: "Dial", data: Uuid + ","),
            };

            Assert.Null(AiAgentChannel.Bind(channels, Uuid));
        }

        [Fact]
        public void Nothing_carrying_the_uuid_is_none()
        {
            Assert.Null(AiAgentChannel.Bind(Array.Empty<ActiveChannel>(), Uuid));
            Assert.Null(AiAgentChannel.Bind(new[] { Channel("PJSIP/a-00000001", "7300", data: Uuid) }, Uuid));
            Assert.Null(AiAgentChannel.Bind(new[] { Channel("PJSIP/a-00000001", "7300", data: ",127.0.0.1:9019") }, ""));
        }

        [Fact]
        public void The_one_channel_in_the_agents_audiosocket_is_found()
        {
            var channels = new[]
            {
                Channel("PJSIP/101-00000001", "7300", application: "Dial"),
                Channel("PJSIP/callcentric-00000002", "7300"),
                Channel("PJSIP/102-00000003", "7400"),
            };

            var found = AiAgentChannel.Find(channels, "7300", out var matches);

            Assert.Equal("PJSIP/callcentric-00000002", found?.Channel);
            Assert.Equal(1, matches);
        }

        /// <summary>The fallback's rule: two calls to the same agent must never be crossed.</summary>
        [Fact]
        public void Two_calls_to_the_same_agent_are_not_told_apart()
        {
            var channels = new[] { Channel("PJSIP/a-00000001", "7300"), Channel("PJSIP/b-00000002", "7300") };

            Assert.Null(AiAgentChannel.Find(channels, "7300", out var matches));
            Assert.Equal(2, matches);
        }

        [Fact]
        public void No_channel_in_the_audiosocket_is_none()
        {
            var channels = new[]
            {
                Channel("PJSIP/a-00000001", "7300", context: "from-trunk"),
                Channel("PJSIP/b-00000002", "7300", application: "Dial"),
            };

            Assert.Null(AiAgentChannel.Find(channels, "7300", out var matches));
            Assert.Equal(0, matches);
        }
    }
}
