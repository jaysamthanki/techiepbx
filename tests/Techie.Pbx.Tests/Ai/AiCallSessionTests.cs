using log4net;
using Techie.Pbx.Core.Ai;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Ai
{
    /// <summary>
    /// One AI agent call's turn loop (D200) with the network replaced by fakes: greeting first,
    /// listen → transcribe → chat → speak, a transfer and a provider failure both ending the call
    /// by closing the socket, and a transfer to a named extension moving the call (D203).
    /// </summary>
    public class AiCallSessionTests
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(AiCallSessionTests));

        private readonly List<IReadOnlyList<(string Role, string Content)>> chats = new();
        private readonly List<string> spoken = new();
        private readonly DuplexTestStream stream = new();
        private readonly List<string> transfers = new();
        private int transcriptions;

        private static readonly IReadOnlyList<(string Extension, string Name)> Staff = new[] { ("101", "Jaysam"), ("102", "Anishi") };

        private static AiChatReply TransferCall(string extension) =>
            new() { ToolCall = new AiToolCall(AiProviders.TransferToolName, $"{{\"extension\":\"{extension}\"}}") };

        private static AiAgent Agent(string greeting = "", string transferTo = "") => new()
        {
            AiAgentID = 1,
            Number = "7300",
            Name = "Reception",
            Prompt = "You are the receptionist.",
            Greeting = greeting,
            TransferTo = transferTo,
        };

        /// <summary>A loud μ-law clip of this many 20 ms frames.</summary>
        private static byte[] Clip(int frames) => Enumerable.Repeat((byte)0x00, frames * Endpointer.WindowSamples).ToArray();

        private static short[] Turn() => TestAudio.Speech(600).Then(TestAudio.Silence(500));

        private static async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);

            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("The condition never became true.");

                await Task.Delay(5);
            }
        }

        private AiCallSession Session(
            AiAgent agent,
            Func<string, string> reply,
            Func<string> heard,
            int replyFrames = 3,
            bool bargeIn = true,
            Exception? transcribeFailure = null,
            bool logTranscripts = true,
            Func<string, AiChatReply>? answer = null,
            IReadOnlyList<(string Extension, string Name)>? targets = null,
            bool transferWorks = true)
        {
            var settings = new AiSettingsSnapshot
            {
                BargeIn = bargeIn,
                LogTranscripts = logTranscripts,
                ElevenLabsApiKey = "test_key_0123456789abcdef",
                ElevenLabsVoiceId = "voice1",
                OpenRouterApiKey = "test_key_0123456789abcdef",
                OpenRouterModel = "vendor/model",
            };

            return new AiCallSession(
                agent,
                settings,
                Log,
                targets ?? [],
                (wav, ct) =>
                {
                    Interlocked.Increment(ref this.transcriptions);
                    Assert.Equal("RIFF"u8.ToArray(), wav[..4]);

                    if (transcribeFailure != null)
                        throw transcribeFailure;

                    return Task.FromResult(heard());
                },
                (messages, model, ct) =>
                {
                    lock (this.chats)
                        this.chats.Add(messages.ToList());

                    return Task.FromResult(answer != null ? answer(messages[^1].Content) : AiChatReply.Text(reply(messages[^1].Content)));
                },
                (text, model, voice, ct) =>
                {
                    lock (this.spoken)
                        this.spoken.Add(text);

                    var frames = this.spoken.Count == 1 && agent.Greeting.Length > 0 ? 5 : replyFrames;
                    return Task.FromResult(Clip(frames));
                },
                (extension, ct) =>
                {
                    lock (this.transfers)
                        this.transfers.Add(extension);

                    return Task.FromResult(transferWorks);
                })
            {
                PlaybackPace = TimeSpan.Zero,
            };
        }

        /// <summary>
        /// The rules the first live calls earned (D200): a model with no facts invents an address, so
        /// the prompt forbids invention outright, and the transfer token is only offered when the
        /// agent actually has somewhere to hand the caller.
        /// </summary>
        [Theory]
        [InlineData("", false)]
        [InlineData("104", true)]
        public void The_system_prompt_forbids_inventing_facts_and_names_the_token_only_with_a_handoff(string transferTo, bool transfer)
        {
            var prompt = AiCallSession.SystemPrompt(Agent(transferTo: transferTo));

            Assert.StartsWith("You are the receptionist.", prompt, StringComparison.Ordinal);
            Assert.Contains("never invent facts", prompt, StringComparison.Ordinal);
            Assert.Equal(transfer, prompt.Contains(AiCallSession.TransferToken, StringComparison.Ordinal));
        }

        [Fact]
        public async Task The_greeting_is_spoken_first_without_asking_the_model()
        {
            var session = this.Session(Agent(greeting: "Thanks for calling."), _ => "unused", () => "unused");

            var run = session.RunAsync(this.stream, CancellationToken.None);
            await this.stream.WaitForFramesAsync(5);
            this.stream.CompleteInbound();
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(new[] { "Thanks for calling." }, this.spoken);
            Assert.Empty(this.chats);

            var frames = this.stream.WrittenFrames;
            Assert.Equal(5, frames.Count);
            Assert.All(frames, f => Assert.Equal(AudioSocketProtocol.Kinds.Audio, f.Kind));
            Assert.All(frames, f => Assert.Equal(Endpointer.WindowSamples * 2, f.Payload.Length));
            Assert.Equal(AiCallOutcome.CallerHungUp, session.Outcome);
            Assert.True(this.stream.IsDisposed);
        }

        /// <summary>
        /// The point of the transcript log (D200): the prompt once per call, then the caller's words
        /// and the agent's reply each turn, so a strange answer can be read against what was asked.
        /// </summary>
        [Fact]
        public async Task The_transcript_carries_the_prompt_the_caller_and_the_reply()
        {
            var session = this.Session(Agent(), _ => "We open at nine.", () => "When do you open?");

            this.stream.SendAudio(Turn());
            var run = session.RunAsync(this.stream, CancellationToken.None);
            await this.stream.WaitForFramesAsync(3);
            this.stream.CompleteInbound();
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains(session.Transcript, l => l.Contains("prompt (", StringComparison.Ordinal) && l.Contains("You are the receptionist.", StringComparison.Ordinal));
            Assert.Contains(session.Transcript, l => l.Contains("turn 1 caller: When do you open?", StringComparison.Ordinal));
            Assert.Contains(session.Transcript, l => l.Contains("turn 1 agent: We open at nine.", StringComparison.Ordinal));
        }

        /// <summary>Switched off, the same call says nothing about what was said.</summary>
        [Fact]
        public async Task The_transcript_is_empty_when_the_setting_is_off()
        {
            var session = this.Session(Agent(), _ => "We open at nine.", () => "When do you open?", logTranscripts: false);

            this.stream.SendAudio(Turn());
            var run = session.RunAsync(this.stream, CancellationToken.None);
            await this.stream.WaitForFramesAsync(3);
            this.stream.CompleteInbound();
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Empty(session.Transcript);
        }

        [Fact]
        public async Task One_turn_is_transcribed_answered_and_spoken()
        {
            var session = this.Session(Agent(), _ => "We open at nine.", () => "When do you open?");

            this.stream.SendAudio(Turn());
            var run = session.RunAsync(this.stream, CancellationToken.None);
            await this.stream.WaitForFramesAsync(3);
            this.stream.CompleteInbound();
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            var chat = Assert.Single(this.chats);
            Assert.Equal("system", chat[0].Role);
            Assert.StartsWith("You are the receptionist.", chat[0].Content, StringComparison.Ordinal);
            Assert.Contains("never invent facts", chat[0].Content, StringComparison.Ordinal);
            Assert.Equal(("user", "When do you open?"), chat[1]);
            Assert.Equal(new[] { "We open at nine." }, this.spoken);
            Assert.Equal(3, this.stream.WrittenFrames.Count);
            Assert.Equal(AiCallOutcome.CallerHungUp, session.Outcome);
        }

        [Fact]
        public async Task A_transfer_speaks_the_preamble_then_closes_the_socket()
        {
            var session = this.Session(
                Agent(transferTo: "Extension:201"),
                _ => "Putting you through now. " + AiCallSession.TransferToken,
                () => "Can I speak to someone?");

            this.stream.SendAudio(Turn());
            await session.RunAsync(this.stream, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(AiCallOutcome.Transferred, session.Outcome);
            Assert.True(this.stream.IsDisposed);
            Assert.Equal(new[] { "Putting you through now." }, this.spoken);
            Assert.Equal(3, this.stream.WrittenFrames.Count);
            Assert.Contains(AiCallSession.TransferToken, Assert.Single(this.chats)[0].Content);
        }

        [Fact]
        public async Task A_bare_transfer_token_closes_the_socket_without_speaking()
        {
            var session = this.Session(Agent(transferTo: "Extension:201"), _ => AiCallSession.TransferToken, () => "A person please.");

            this.stream.SendAudio(Turn());
            await session.RunAsync(this.stream, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(AiCallOutcome.Transferred, session.Outcome);
            Assert.Empty(this.spoken);
            Assert.Empty(this.stream.WrittenFrames);
        }

        [Fact]
        public async Task A_provider_failure_closes_the_socket()
        {
            var session = this.Session(
                Agent(),
                _ => "unused",
                () => "unused",
                transcribeFailure: new AiProviderException("ElevenLabs speech-to-text answered 401 Unauthorized."));

            this.stream.SendAudio(Turn());
            await session.RunAsync(this.stream, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(AiCallOutcome.ProviderFailed, session.Outcome);
            Assert.True(this.stream.IsDisposed);
            Assert.Empty(this.chats);
            Assert.Empty(this.stream.WrittenFrames);
        }

        [Fact]
        public async Task Three_silent_turns_in_a_row_close_the_socket()
        {
            var session = this.Session(Agent(), _ => "unused", () => "  ");

            this.stream.SendAudio(Turn().Then(Turn()).Then(Turn()));
            await session.RunAsync(this.stream, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(AiCallOutcome.Silent, session.Outcome);
            Assert.Equal(AiCallSession.MaxSilentTurns, this.transcriptions);
            Assert.Empty(this.chats);
        }

        [Fact]
        public async Task A_hangup_frame_ends_the_call()
        {
            var session = this.Session(Agent(), _ => "unused", () => "unused");

            this.stream.SendAudio(TestAudio.Silence(200));
            this.stream.SendHangup();

            await session.RunAsync(this.stream, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(AiCallOutcome.CallerHungUp, session.Outcome);
            Assert.Equal(0, this.transcriptions);
        }

        [Fact]
        public async Task The_caller_speaking_over_the_agent_stops_its_speech()
        {
            var agent = Agent(greeting: "Hello, you have reached the front desk.");
            var session = this.Session(agent, _ => "Sure.", () => "Sorry, I need billing.", replyFrames: 1);
            session.PlaybackPace = TimeSpan.FromMilliseconds(Endpointer.WindowMs);

            var run = session.RunAsync(this.stream, CancellationToken.None);

            // The greeting is 5 frames here; interrupting after the first leaves the rest unsaid.
            await this.stream.WaitForFramesAsync(1);
            this.stream.SendAudio(Turn());
            await WaitUntil(() => this.spoken.Count == 2);
            await Task.Delay(100);
            this.stream.CompleteInbound();
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(2, this.spoken.Count);
            Assert.True(this.stream.WrittenFrames.Count < 5 + 1, $"{this.stream.WrittenFrames.Count} frames were written");
            Assert.Single(this.chats);
        }

        [Fact]
        public void The_transfer_rule_is_only_offered_when_there_is_somewhere_to_transfer_to()
        {
            Assert.DoesNotContain(AiCallSession.TransferToken, AiCallSession.SystemPrompt(Agent()));
            Assert.Contains(AiCallSession.TransferToken, AiCallSession.SystemPrompt(Agent(transferTo: "Extension:201")));
            Assert.StartsWith("You are the receptionist.", AiCallSession.SystemPrompt(Agent(transferTo: "Extension:201")));
        }

        /// <summary>
        /// With extensions to offer, the model is told about the tool, not the token: the token is
        /// the fallback for an agent with nobody to name (D203).
        /// </summary>
        [Fact]
        public void With_a_transfer_tool_the_prompt_names_the_tool_and_not_the_token()
        {
            var prompt = AiCallSession.SystemPrompt(Agent(transferTo: "Extension:201"), transferTool: true);

            Assert.Contains(AiProviders.TransferToolName, prompt, StringComparison.Ordinal);
            Assert.DoesNotContain(AiCallSession.TransferToken, prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_transfer_call_says_the_transfer_line_and_moves_the_call()
        {
            var agent = Agent();
            agent.TransferLine = "Hold on, connecting you.";
            var session = this.Session(agent, _ => "unused", () => "Can I speak to Anishi?", answer: _ => TransferCall("102"), targets: Staff);

            this.stream.SendAudio(Turn());
            await session.RunAsync(this.stream, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(AiCallOutcome.Transferred, session.Outcome);
            Assert.Equal(new[] { "102" }, this.transfers);
            Assert.Equal(new[] { "Hold on, connecting you." }, this.spoken);
            Assert.Equal(3, this.stream.WrittenFrames.Count);
            Assert.Contains(AiProviders.TransferToolName, Assert.Single(this.chats)[0].Content);
        }

        [Fact]
        public async Task A_blank_transfer_line_is_the_default()
        {
            var session = this.Session(Agent(), _ => "unused", () => "Anishi please.", answer: _ => TransferCall("102"), targets: Staff);

            this.stream.SendAudio(Turn());
            await session.RunAsync(this.stream, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(new[] { AiAgent.DefaultTransferLine }, this.spoken);
        }

        /// <summary>
        /// A redirect that did not happen leaves the caller with the agent: told so in one line,
        /// with the model told too, and the conversation carries on to the next turn.
        /// </summary>
        [Fact]
        public async Task A_failed_transfer_says_so_and_the_conversation_carries_on()
        {
            var turns = 0;
            var session = this.Session(
                Agent(),
                _ => "unused",
                () => "Anishi please.",
                answer: _ => ++turns == 1 ? TransferCall("102") : AiChatReply.Text("Of course."),
                targets: Staff,
                transferWorks: false);

            this.stream.SendAudio(Turn());
            var run = session.RunAsync(this.stream, CancellationToken.None);

            // The second turn is sent once both lines are out: audio that arrives while the agent
            // is about to speak is thrown away, as it is on a real call.
            await this.stream.WaitForFramesAsync(6);
            this.stream.SendAudio(Turn());
            await WaitUntil(() => this.chats.Count == 2 && this.spoken.Count == 3);
            this.stream.CompleteInbound();
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(AiCallOutcome.CallerHungUp, session.Outcome);
            Assert.Equal(new[] { "102" }, this.transfers);
            Assert.Equal(new[] { AiAgent.DefaultTransferLine, AiCallSession.TransferFailedLine, "Of course." }, this.spoken);
            Assert.Contains(this.chats[1], m => m.Role == "system" && m.Content.Contains("did not go through", StringComparison.Ordinal));
            Assert.Contains(this.chats[1], m => m == ("assistant", AiCallSession.TransferFailedLine));
        }

        /// <summary>The enum should prevent it, but a model naming an extension it was not offered is ignored.</summary>
        [Fact]
        public async Task A_transfer_to_an_extension_not_offered_is_ignored()
        {
            var session = this.Session(
                Agent(),
                _ => "unused",
                () => "Put me through to 999.",
                answer: _ => TransferCall("999") with { Content = "I can't do that." },
                targets: Staff);

            this.stream.SendAudio(Turn());
            var run = session.RunAsync(this.stream, CancellationToken.None);
            await this.stream.WaitForFramesAsync(3);
            this.stream.CompleteInbound();
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Empty(this.transfers);
            Assert.Equal(new[] { "I can't do that." }, this.spoken);
            Assert.Equal(AiCallOutcome.CallerHungUp, session.Outcome);
        }
    }
}
