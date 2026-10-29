using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What an AI agent turns into (D199): one entry in the internal context that answers and
    /// hands the audio to the loopback media service, then hangs up or hands the caller on.
    /// </summary>
    public class AiAgentRendererTests
    {
        private static List<Extension> SampleExtensions() => new()
        {
            new Extension { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
        };

        private static List<AiAgent> SampleAgents() => new()
        {
            new AiAgent
            {
                AiAgentID = 1,
                Number = "7300",
                Name = "Reception",
                Prompt = "You are the receptionist.",
                TransferTo = "1001",
            },
            new AiAgent
            {
                AiAgentID = 2,
                Number = "7301",
                Name = "Switched off",
                Prompt = "Never rendered.",
                Enabled = false,
            },
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        private static string Render(IEnumerable<AiAgent> agents, int port = AiSettings.DefaultMediaPort) =>
            ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                new List<RingGroup>(), new List<Announcement>(), new List<Ivr>(), new List<TimeCondition>(),
                AsteriskSettings.DefaultTimezone, new ParkingSettings(), new List<MohClass>(), new List<CallFlowControl>(),
                agents, port);

        [Fact]
        public void Ai_agents_match_expected_file()
        {
            Assert.Equal(Expected("extensions-ai-agents.conf"), Render(SampleAgents()));
        }

        /// <summary>The lab has no agents: its dialplan must not change by one byte.</summary>
        [Fact]
        public void A_system_with_no_agents_renders_exactly_what_it_did_before()
        {
            var before = ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                new List<RingGroup>(), new List<Announcement>(), new List<Ivr>(), new List<TimeCondition>(),
                AsteriskSettings.DefaultTimezone, new ParkingSettings(), new List<MohClass>(), new List<CallFlowControl>());

            Assert.Equal(before, Render(new List<AiAgent>()));
            Assert.DoesNotContain("AudioSocket", before);
        }

        [Fact]
        public void An_agent_without_a_handoff_hangs_up_and_the_port_is_the_setting()
        {
            var agent = SampleAgents()[0];
            agent.TransferTo = "";

            var actual = Render(new[] { agent }, 9100);

            Assert.Contains($" same => n,AudioSocket({agent.ServiceUuid},127.0.0.1:9100)\n same => n,Hangup()\n", actual);
        }

        [Fact]
        public void A_port_out_of_range_is_refused()
        {
            Assert.Throws<InvalidOperationException>(() => Render(SampleAgents(), 80));
        }

        [Fact]
        public void An_invalid_agent_is_refused()
        {
            var agent = SampleAgents()[0];
            agent.Name = "Bad; name";

            Assert.Throws<InvalidOperationException>(() => Render(new[] { agent }));
        }
    }
}
