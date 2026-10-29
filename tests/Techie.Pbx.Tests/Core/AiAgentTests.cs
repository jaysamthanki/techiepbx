using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>The AI agent's own rules (F11, D199): what the model refuses without a database.</summary>
    public class AiAgentTests
    {
        private static AiAgent Agent() => new()
        {
            AiAgentID = 1,
            Number = "7300",
            Name = "Reception",
            Prompt = "You are the receptionist for Example Ltd.",
        };

        [Fact]
        public void A_minimal_agent_is_valid()
        {
            Assert.Empty(Agent().Validate());
        }

        [Fact]
        public void A_full_agent_is_valid()
        {
            var agent = Agent();
            agent.Greeting = "Thanks for calling Example, how can I help?";
            agent.VoiceId = "voice_0123.abc-DEF";
            agent.Model = "vendor/model-name:free";
            agent.TransferTo = "RingGroup:600";
            agent.Prompt = "Line one.\nLine two.";

            Assert.Empty(agent.Validate());
        }

        [Theory]
        [InlineData("1")]
        [InlineData("1234567")]
        [InlineData("12a4")]
        [InlineData("")]
        public void A_number_that_is_not_2_to_6_digits_is_refused(string number)
        {
            var agent = Agent();
            agent.Number = number;

            Assert.Contains("Number must be 2 to 6 digits.", agent.Validate());
        }

        [Theory]
        [InlineData("")]
        [InlineData("Bad; name")]
        [InlineData("Bad\"name")]
        public void A_bad_name_is_refused(string name)
        {
            var agent = Agent();
            agent.Name = name;

            Assert.NotEmpty(agent.Validate());
        }

        [Fact]
        public void A_name_over_64_characters_is_refused()
        {
            var agent = Agent();
            agent.Name = new string('a', 65);

            Assert.Contains("Name must be 64 characters or fewer.", agent.Validate());
        }

        [Fact]
        public void The_prompt_is_required_and_bounded()
        {
            var agent = Agent();
            agent.Prompt = "  ";
            Assert.NotEmpty(agent.Validate());

            agent.Prompt = new string('a', AiAgent.MaxPromptLength);
            Assert.Empty(agent.Validate());

            agent.Prompt = new string('a', AiAgent.MaxPromptLength + 1);
            Assert.NotEmpty(agent.Validate());
        }

        [Theory]
        [InlineData("Hello\nthere")]
        [InlineData("Tab\there")]
        public void A_greeting_with_control_characters_is_refused(string greeting)
        {
            var agent = Agent();
            agent.Greeting = greeting;

            Assert.Contains("Greeting must be one line of plain text.", agent.Validate());
        }

        [Fact]
        public void A_greeting_over_the_limit_is_refused()
        {
            var agent = Agent();
            agent.Greeting = new string('a', AiAgent.MaxGreetingLength + 1);

            Assert.NotEmpty(agent.Validate());
        }

        [Theory]
        [InlineData("has space")]
        [InlineData("slash/not-allowed")]
        [InlineData("semi;colon")]
        public void A_badly_shaped_voice_is_refused(string voice)
        {
            var agent = Agent();
            agent.VoiceId = voice;

            Assert.NotEmpty(agent.Validate());
        }

        [Theory]
        [InlineData("has space")]
        [InlineData("semi;colon")]
        [InlineData("bracket[x]")]
        public void A_badly_shaped_model_is_refused(string model)
        {
            var agent = Agent();
            agent.Model = model;

            Assert.NotEmpty(agent.Validate());
        }

        [Fact]
        public void A_voice_or_model_over_its_length_is_refused()
        {
            var agent = Agent();
            agent.VoiceId = new string('a', 65);
            Assert.NotEmpty(agent.Validate());

            agent.VoiceId = "";
            agent.Model = new string('a', 128);
            Assert.NotEmpty(agent.Validate());
        }

        [Theory]
        [InlineData("Extension:1001")]
        [InlineData("RingGroup:600")]
        [InlineData("Voicemail:1001")]
        [InlineData("Ivr:7200")]
        [InlineData("CallFlowControl:*28")]
        public void A_transfer_to_any_destination_is_valid(string target)
        {
            var agent = Agent();
            agent.TransferTo = target;

            Assert.Empty(agent.Validate());
            Assert.Equal(target, agent.ToTransferDestination()!.Key);
        }

        [Theory]
        [InlineData("1001")]
        [InlineData("Extension:1")]
        [InlineData("Nowhere:1001")]
        [InlineData("+15551234567")]
        public void A_transfer_that_is_not_a_destination_is_refused(string target)
        {
            var agent = Agent();
            agent.TransferTo = target;

            Assert.Contains("Transfer to must be a destination, or blank for none.", agent.Validate());
            Assert.Null(agent.ToTransferDestination());
        }

        [Theory]
        [InlineData("AiAgent:7300")]
        [InlineData("AiAgent:7301")]
        public void An_agent_cannot_transfer_to_an_agent(string target)
        {
            var agent = Agent();
            agent.TransferTo = target;

            Assert.Contains("An agent cannot transfer a call to an AI agent.", agent.Validate());
        }

        [Fact]
        public void No_transfer_has_no_transfer_destination()
        {
            Assert.Null(Agent().ToTransferDestination());
        }

        [Fact]
        public void The_destination_is_the_agent_number()
        {
            Assert.Equal("AiAgent:7300", Agent().ToDestination().Key);
        }

        [Fact]
        public void Playable_means_enabled_with_a_number()
        {
            var agent = Agent();
            Assert.True(agent.IsPlayable);

            agent.Enabled = false;
            Assert.False(agent.IsPlayable);
        }

        /// <summary>
        /// AudioSocket() needs a real UUID, and the media service needs the same one every apply
        /// and after a renumber: it is derived from the row ID and nothing else (D199).
        /// </summary>
        [Fact]
        public void The_service_uuid_is_a_stable_uuid_from_the_row_id()
        {
            var agent = Agent();
            var uuid = agent.ServiceUuid;

            Assert.True(Guid.TryParseExact(uuid, "D", out _));
            Assert.Equal(uuid, Agent().ServiceUuid);

            agent.Number = "7301";
            Assert.Equal(uuid, agent.ServiceUuid);

            agent.AiAgentID = 2;
            Assert.NotEqual(uuid, agent.ServiceUuid);
        }
    }
}
