using Techie.Pbx.Asterisk.Ami;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Which channel an AI agent's transfer moves (D203): the one in its AudioSocket, and none at
    /// all when two callers are talking to the same agent.
    /// </summary>
    public class AiAgentChannelTests
    {
        private static ActiveChannel Channel(string name, string exten, string application = "AudioSocket", string context = "internal") =>
            new() { Channel = name, Exten = exten, Application = application, Context = context };

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

        /// <summary>The ambiguity rule: two calls to the same agent must never be crossed.</summary>
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
