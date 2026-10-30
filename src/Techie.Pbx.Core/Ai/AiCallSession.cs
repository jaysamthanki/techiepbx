using System.Diagnostics;
using System.Threading.Channels;
using log4net;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// One AI agent call on its AudioSocket (F11, D199, D200): greet, then listen → transcribe →
    /// chat → speak, turn after turn, until the caller hangs up or the call has to end. Every way
    /// the agent ends a call is the same act — it closes the socket — and the dialplan decides
    /// what follows: the agent's handoff extension if it has one, otherwise a hangup (D199).
    ///
    /// The one exception is a transfer to a named extension (D203): the model calls
    /// <see cref="AiProviders.TransferToolName"/>, and the channel is moved from outside, by AMI,
    /// because the socket has no way to tell the dialplan where to go.
    ///
    /// The network is five delegates, so the whole turn loop is testable with fakes. What the caller
    /// says and what the model replies is logged only when <c>Ai.LogTranscripts</c> is on (D200).
    /// </summary>
    public class AiCallSession
    {
        /// <summary>How many earlier messages (caller and agent together) go with each chat request.</summary>
        public const int HistoryMessages = 12;

        /// <summary>Turns in a row with nothing transcribed before the agent gives up on the call.</summary>
        public const int MaxSilentTurns = 3;

        /// <summary>The phrases a model uses instead of calling the tool (D206). Narrow on purpose.</summary>
        private static readonly string[] Promises =
        {
            "get you over to",
            "put you through",
            "transfer you",
            "transferring you",
            "connecting you",
            "connect you to",
            "just a moment",
            "hold on",
        };

        /// <summary>Words that turn a transfer phrase into a refusal, which is not a promise (D206).</summary>
        private static readonly string[] Negations =
        {
            "can't",
            "cannot",
            "can not",
            "unable",
            "don't have",
            "do not have",
            "not able",
            "won't be able",
        };

        /// <summary>What the agent says when a transfer to an extension did not happen (D203).</summary>
        public const string TransferFailedLine = "I couldn't reach them — can I help another way?";

        /// <summary>
        /// The token the model writes to ask for the configured handoff. Stripped before the reply
        /// is spoken. Offered only to an agent with no extensions to transfer to (D203).
        /// </summary>
        public const string TransferToken = "[TRANSFER]";

        /// <summary>The longest call an agent will hold; then the socket is closed and the dialplan carries on.</summary>
        public static readonly TimeSpan MaxCallDuration = TimeSpan.FromMinutes(10);

        /// <summary>How much of one turn's words a transcript line carries, so one long reply cannot flood the log.</summary>
        public const int MaxLoggedText = 500;

        private readonly AiAgent agent;
        private readonly string? channel;
        private readonly Func<IReadOnlyList<(string Role, string Content)>, string, CancellationToken, Task<AiChatReply>> chat;
        private readonly Endpointer endpointer = new();
        private readonly Func<CancellationToken, Task<string?>> findChannel;
        private readonly Channel<AudioSocketFrame> frames = Channel.CreateUnbounded<AudioSocketFrame>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        private readonly List<(string Role, string Content)> history = new();
        private readonly ILog log;
        private readonly Func<string, string, CancellationToken, Task<bool>> redirect;
        private readonly List<string> transcript = new();
        private readonly AiSettingsSnapshot settings;
        private readonly Func<string, string, string, CancellationToken, Task<byte[]>> speak;
        private readonly Func<byte[], CancellationToken, Task<string>> transcribe;
        private readonly IReadOnlyList<(string Extension, string Name)> transferTargets;

        /// <summary>How the call ended; meaningful once <see cref="RunAsync"/> has returned.</summary>
        public AiCallOutcome Outcome { get; private set; } = AiCallOutcome.CallerHungUp;

        /// <summary>
        /// The time between 20 ms chunks of the agent's speech. Real time, because Asterisk writes
        /// each frame to the channel the moment it arrives and a phone cannot buffer a sentence
        /// sent all at once. Tests set it to zero.
        /// </summary>
        public TimeSpan PlaybackPace { get; set; } = TimeSpan.FromMilliseconds(Endpointer.WindowMs);

        /// <summary>
        /// <paramref name="speak"/> takes the text, the TTS model and the voice ID, and returns
        /// μ-law 8 kHz bytes; <paramref name="chat"/> takes the messages and the model slug, and
        /// offers the model <paramref name="transferTargets"/> as its transfer tool.
        /// <paramref name="redirect"/> takes a channel name and one of those extensions and says
        /// whether the call was moved there (D203); with no targets it is never called.
        /// <paramref name="channel"/> is this call's channel, bound by its UUID (D208), and is the
        /// one redirected; when it is null <paramref name="findChannel"/> is asked for it at
        /// transfer time instead, and null from that is a failed transfer.
        /// </summary>
        public AiCallSession(
            AiAgent agent,
            string? channel,
            AiSettingsSnapshot settings,
            ILog log,
            IReadOnlyList<(string Extension, string Name)> transferTargets,
            Func<byte[], CancellationToken, Task<string>> transcribe,
            Func<IReadOnlyList<(string Role, string Content)>, string, CancellationToken, Task<AiChatReply>> chat,
            Func<string, string, string, CancellationToken, Task<byte[]>> speak,
            Func<CancellationToken, Task<string?>> findChannel,
            Func<string, string, CancellationToken, Task<bool>> redirect)
        {
            this.agent = agent;
            this.channel = channel;
            this.chat = chat;
            this.findChannel = findChannel;
            this.log = log;
            this.redirect = redirect;
            this.settings = settings;
            this.speak = speak;
            this.transcribe = transcribe;
            this.transferTargets = transferTargets;
        }

        /// <summary>
        /// Runs the call to its end and closes the socket. The caller's first frame (the UUID) has
        /// already been read by whoever accepted the connection. Never throws for anything a call
        /// can do; <see cref="Outcome"/> says how it ended.
        /// </summary>
        public async Task RunAsync(Stream socket, CancellationToken cancellationToken)
        {
            using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            call.CancelAfter(MaxCallDuration);

            var pump = this.PumpAsync(socket, call.Token);

            try
            {
                this.Outcome = await this.ConverseAsync(socket, call.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                this.Outcome = AiCallOutcome.Stopped;
            }
            catch (OperationCanceledException) when (call.IsCancellationRequested)
            {
                this.Outcome = AiCallOutcome.TimedOut;
            }
            catch (AiProviderException ex)
            {
                this.log.Warn($"AI agent {this.agent.Number}: {ex.Message} Handing the call to the dialplan.");
                this.Outcome = AiCallOutcome.ProviderFailed;
            }
            catch (IOException)
            {
                // A write to a socket Asterisk has already closed: the caller is gone.
                this.Outcome = AiCallOutcome.CallerHungUp;
            }
            catch (Exception ex)
            {
                this.log.Error($"AI agent {this.agent.Number}: the call failed ({ex.GetType().Name}). Handing the call to the dialplan.", ex);
                this.Outcome = AiCallOutcome.Failed;
            }
            finally
            {
                call.Cancel();
                socket.Dispose();
                await pump;
            }
        }

        /// <summary>
        /// The system prompt the model sees: the agent's own, and the rule for handing a caller on.
        /// With a transfer tool that is the tool (D203); without one, and only when the agent has
        /// somewhere to hand a caller, it is the <see cref="TransferToken"/>.
        /// </summary>
        public static string SystemPrompt(AiAgent agent, bool transferTool = false)
        {
            // The rule that answers the failure the first live calls showed: asked where the office
            // is, a model with no facts invents an address. A receptionist that says it does not know
            // and offers the handoff is useful; one that invents a street is a liability.
            const string Rules =
                "\n\nRules: never invent facts about this business — no address, opening hours, prices, " +
                "policies or names you were not given above. If you do not know something, say so in one " +
                "short sentence. Keep every reply to one or two short spoken sentences.";

            // The words before a transfer are the agent's canned line, so the model is asked for
            // none of its own: a model that also said "putting you through" would say it twice.
            if (transferTool)
            {
                return agent.Prompt.TrimEnd() + Rules +
                    $"\n\nIf the caller asks to speak to a particular person, call the {AiProviders.TransferToolName} " +
                    "tool with their extension and say nothing at all. Do not transfer for a question you can answer. " +
                    "Never say you are transferring, connecting or putting the caller through, and never say \"just a " +
                    "moment\": the tool is the transfer, and the system speaks the handoff line itself. A spoken " +
                    "promise with no tool call leaves the caller waiting on a call nobody moved (D206).";
            }

            if (agent.TransferTo.Length == 0)
                return agent.Prompt.TrimEnd() + Rules;

            return agent.Prompt.TrimEnd() + Rules +
                "\n\nIf the caller asks for a person, or asks something you cannot answer, a transfer to a member " +
                $"of staff is available: end your reply with the exact token {TransferToken} on its own. Do not " +
                "transfer for a question you can answer, and do not say goodbye without the token.";
        }

        /// <summary>
        /// The lines this call wrote to the log about what was said (D200): the prompt, then each
        /// turn's caller and agent words. Empty when <c>Ai.LogTranscripts</c> is off. Kept here as
        /// well as logged so a test can read exactly what an operator would see.
        /// </summary>
        public IReadOnlyList<string> Transcript => this.transcript;

        /// <summary>
        /// Writes one transcript line: remembered for the caller of this class, and logged once. The
        /// words come from the caller and the model, so they go out unchanged (flattened).
        /// </summary>
        private void Note(string line)
        {
            this.transcript.Add(line);
            this.log.Info(line);
        }

        /// <summary>The turn loop, returning how the call should end.</summary>
        private async Task<AiCallOutcome> ConverseAsync(Stream socket, CancellationToken cancellationToken)
        {
            var model = this.settings.ModelFor(this.agent.Model);
            var silentTurns = 0;
            var turn = 0;
            var systemPrompt = SystemPrompt(this.agent, this.transferTargets.Count > 0);

            // The prompt is logged once per call: it is what the model is being asked to be, so a
            // strange reply is read against it rather than guessed about (D200).
            if (this.settings.LogTranscripts)
                this.Note($"AI agent {this.agent.Number}: prompt ({systemPrompt.Length} chars): {Flat(systemPrompt)}");

            if (!string.IsNullOrWhiteSpace(this.agent.Greeting))
            {
                if ((await this.SayAsync(socket, this.agent.Greeting.Trim(), this.settings.BargeIn, cancellationToken)).Playback == Playback.CallerGone)
                    return AiCallOutcome.CallerHungUp;
            }

            while (true)
            {
                var utterance = await this.ListenAsync(cancellationToken);

                if (utterance == null)
                    return AiCallOutcome.CallerHungUp;

                var clock = Stopwatch.StartNew();
                var heard = await this.transcribe(WavWriter.ToWav(utterance, Endpointer.SampleRateHz), cancellationToken);
                var transcribeMs = clock.ElapsedMilliseconds;

                if (string.IsNullOrWhiteSpace(heard))
                {
                    if (++silentTurns >= MaxSilentTurns)
                        return AiCallOutcome.Silent;

                    continue;
                }

                silentTurns = 0;
                turn++;
                this.Remember("user", heard.Trim());

                if (this.settings.LogTranscripts)
                    this.Note($"AI agent {this.agent.Number}: turn {turn} caller: {Flat(heard)}");

                var messages = new List<(string Role, string Content)> { ("system", systemPrompt) };
                messages.AddRange(this.history);

                clock.Restart();
                var answer = await this.chat(messages, model, cancellationToken);
                var chatMs = clock.ElapsedMilliseconds;

                // A model that promised a transfer in words but never called the tool leaves the caller
                // waiting on a line nobody moved. The promise is honoured the only way left: the call
                // follows the agent's own handoff, the same one a closed socket reaches (D206).
                if (answer.ToolCall == null && this.agent.TransferTo.Length > 0 && LooksLikeTransferPromise(answer.Content))
                {
                    this.Note($"AI agent {this.agent.Number}: turn {turn} spoke a transfer promise with no tool call; handing the call off instead.");
                    this.Remember("assistant", answer.Content.Trim());

                    var said = await this.SayAsync(socket, answer.Content.Trim(), bargeIn: false, cancellationToken: cancellationToken);

                    return said.Playback == Playback.CallerGone ? AiCallOutcome.CallerHungUp : AiCallOutcome.Transferred;
                }

                if (answer.ToolCall != null)
                {
                    var target = this.TransferTarget(answer.ToolCall);

                    if (target != null)
                    {
                        var ended = await this.TransferAsync(socket, target.Value, turn, cancellationToken);

                        if (ended != null)
                            return ended.Value;

                        continue;
                    }
                }

                var reply = answer.Content;
                var transfer = reply.Contains(TransferToken, StringComparison.Ordinal);
                var spoken = reply.Replace(TransferToken, "", StringComparison.Ordinal).Trim();

                if (this.settings.LogTranscripts && spoken.Length > 0)
                    this.Note($"AI agent {this.agent.Number}: turn {turn} agent: {Flat(spoken)}{(transfer ? " [transfer]" : "")}");

                if (spoken.Length > 0)
                {
                    this.Remember("assistant", spoken);

                    // A goodbye before a transfer is played out whole: the caller is leaving either way.
                    var said = await this.SayAsync(socket, spoken, this.settings.BargeIn && !transfer, cancellationToken);

                    // One line per turn, measuring what the caller actually waits through: the model's
                    // time to first speech is transcribe + chat + speak, and the playback that follows
                    // runs at real time. Counts and milliseconds only — never the words (D200).
                    this.log.Info(
                        $"AI agent {this.agent.Number}: turn {turn} - heard {heard.Trim().Length} chars in {transcribeMs} ms, " +
                        $"replied {spoken.Length} chars in {chatMs} ms, speech {said.SpeechMs} ms");

                    if (said.Playback == Playback.CallerGone)
                        return AiCallOutcome.CallerHungUp;
                }

                if (transfer)
                    return AiCallOutcome.Transferred;
            }
        }

        /// <summary>
        /// Takes every frame already waiting without blocking. Reports the caller gone, a barge-in
        /// (with the loud samples), or nothing worth stopping for.
        /// </summary>
        private Playback Drain(bool bargeIn, out short[] interruption)
        {
            interruption = [];
            var reader = this.frames.Reader;

            while (reader.TryRead(out var frame))
            {
                if (frame.Kind == AudioSocketProtocol.Kinds.Hangup)
                    return Playback.CallerGone;

                if (!bargeIn || frame.Kind != AudioSocketProtocol.Kinds.Audio)
                    continue;

                var samples = AudioSocketProtocol.Samples(frame.Payload);

                if (Endpointer.IsSpeech(samples))
                {
                    interruption = samples;
                    return Playback.BargedIn;
                }
            }

            return reader.Completion.IsCompleted ? Playback.CallerGone : Playback.Finished;
        }

        /// <summary>
        /// Audio frames until the endpointer completes a turn, or null when the caller has gone.
        /// The endpointer is not reset first: a barge-in has already seeded it.
        /// </summary>
        private async Task<short[]?> ListenAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var frame = await this.NextFrameAsync(cancellationToken);

                if (frame == null || frame.Kind == AudioSocketProtocol.Kinds.Hangup)
                    return null;

                if (frame.Kind == AudioSocketProtocol.Kinds.Error)
                {
                    this.log.Warn($"AI agent {this.agent.Number}: Asterisk reported an AudioSocket error.");
                    continue;
                }

                if (frame.Kind == AudioSocketProtocol.Kinds.Audio && this.endpointer.Add(AudioSocketProtocol.Samples(frame.Payload)))
                    return this.endpointer.Take();
            }
        }

        private async Task<AudioSocketFrame?> NextFrameAsync(CancellationToken cancellationToken)
        {
            var reader = this.frames.Reader;

            while (await reader.WaitToReadAsync(cancellationToken))
            {
                if (reader.TryRead(out var frame))
                    return frame;
            }

            return null;
        }

        /// <summary>
        /// Writes the speech in real-time 20 ms chunks. Between chunks the caller's frames are read:
        /// with barge-in on, the first loud one stops playback and seeds the next utterance; with
        /// it off they are read and thrown away. Audio that queued up while the agent was thinking
        /// is discarded first, so the caller's pause before the reply cannot cut the reply off.
        /// </summary>
        private async Task<Playback> PlayAsync(Stream socket, short[] pcm, bool bargeIn, CancellationToken cancellationToken)
        {
            if (this.Drain(bargeIn: false, out _) == Playback.CallerGone)
                return Playback.CallerGone;

            var clock = Stopwatch.StartNew();

            for (int offset = 0, chunk = 0; offset < pcm.Length; offset += Endpointer.WindowSamples, chunk++)
            {
                var wait = this.PlaybackPace * chunk - clock.Elapsed;

                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancellationToken);

                var heard = this.Drain(bargeIn, out var interruption);

                if (heard == Playback.CallerGone)
                    return Playback.CallerGone;

                if (heard == Playback.BargedIn)
                {
                    this.endpointer.Reset();
                    this.endpointer.Add(interruption);
                    return Playback.BargedIn;
                }

                var length = Math.Min(Endpointer.WindowSamples, pcm.Length - offset);
                await AudioSocketProtocol.WriteAudioAsync(socket, pcm.AsSpan(offset, length), cancellationToken);
            }

            return Playback.Finished;
        }

        /// <summary>
        /// Reads frames off the socket into the queue until it ends, so the turn loop can look at
        /// the caller's audio while it is writing its own. A protocol error is the end of the call.
        /// </summary>
        private async Task PumpAsync(Stream socket, CancellationToken cancellationToken)
        {
            await Task.Yield();

            try
            {
                while (true)
                {
                    var frame = await AudioSocketProtocol.ReadFrameAsync(socket, cancellationToken);

                    if (frame == null)
                        break;

                    this.frames.Writer.TryWrite(frame);
                }
            }
            catch (InvalidDataException ex)
            {
                this.log.Warn($"AI agent {this.agent.Number}: {ex.Message} Ending the call.");
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // The call is over, from one end or the other.
            }
            finally
            {
                this.frames.Writer.TryComplete();
            }
        }

        /// <summary>Keeps the conversation to the last <see cref="HistoryMessages"/> messages.</summary>
        private void Remember(string role, string content)
        {
            this.history.Add((role, content));

            if (this.history.Count > HistoryMessages)
                this.history.RemoveRange(0, this.history.Count - HistoryMessages);
        }

        private async Task<Spoken> SayAsync(Stream socket, string text, bool bargeIn, CancellationToken cancellationToken)
        {
            var voice = this.agent.VoiceId.Length > 0 ? this.agent.VoiceId : this.settings.ElevenLabsVoiceId;

            var clock = Stopwatch.StartNew();
            var audio = await this.speak(text, this.settings.ElevenLabsModel, voice, cancellationToken);
            var speechMs = clock.ElapsedMilliseconds;

            return new Spoken(await this.PlayAsync(socket, MuLaw.FromMuLaw(audio), bargeIn, cancellationToken), speechMs);
        }

        /// <summary>
        /// Puts the caller through to an extension the model chose (D203). The line is said first:
        /// once the channel is redirected Asterisk ends <c>AudioSocket()</c>, so anything said
        /// after it would never reach the caller. Returns the call's outcome when it is over, or
        /// null when the transfer failed and the conversation carries on.
        ///
        /// On success the call has already left this agent's extension: closing the socket ends
        /// nothing, and the dialplan line after <c>AudioSocket()</c> — the configured handoff — is
        /// never reached, because the channel is running at the new extension instead.
        /// </summary>
        private async Task<AiCallOutcome?> TransferAsync(
            Stream socket,
            (string Extension, string Name) target,
            int turn,
            CancellationToken cancellationToken)
        {
            this.log.Info($"AI agent {this.agent.Number}: transfer to {target.Extension} ({target.Name}) requested");

            var line = this.agent.TransferLine.Trim().Length > 0 ? this.agent.TransferLine.Trim() : AiAgent.DefaultTransferLine;

            if (this.settings.LogTranscripts)
                this.Note($"AI agent {this.agent.Number}: turn {turn} agent: {Flat(line)} [transfer to {target.Extension}]");

            // Played out whole: the caller is about to leave either way.
            if ((await this.SayAsync(socket, line, bargeIn: false, cancellationToken)).Playback == Playback.CallerGone)
                return AiCallOutcome.CallerHungUp;

            // The bound channel is redirected as it is (D208); only a session given none asks Asterisk.
            var channel = this.channel ?? await this.findChannel(cancellationToken);

            if (channel != null && await this.redirect(channel, target.Extension, cancellationToken))
                return AiCallOutcome.Transferred;

            // Why it failed is the listener's to log: it is the one that talked to Asterisk.
            this.log.Info($"AI agent {this.agent.Number}: transfer to {target.Extension} ({target.Name}) did not happen; the conversation carries on.");

            // For the model only: the caller hears the failure line, never this.
            this.Remember("system", $"The transfer to {target.Extension} ({target.Name}) did not go through. The caller has been told.");
            this.Remember("assistant", TransferFailedLine);

            if (this.settings.LogTranscripts)
                this.Note($"AI agent {this.agent.Number}: turn {turn} agent: {TransferFailedLine}");

            var said = await this.SayAsync(socket, TransferFailedLine, this.settings.BargeIn, cancellationToken);

            return said.Playback == Playback.CallerGone ? AiCallOutcome.CallerHungUp : null;
        }

        /// <summary>
        /// The extension a tool call asks for, when it is the transfer tool and the extension is one
        /// the agent was offered; otherwise null, and the call is ignored. The enum should make an
        /// unknown extension impossible, but the model's output is not trusted to follow it.
        /// </summary>
        private (string Extension, string Name)? TransferTarget(AiToolCall call)
        {
            var extension = string.Equals(call.Name, AiProviders.TransferToolName, StringComparison.Ordinal)
                ? call.Argument("extension")
                : null;

            foreach (var target in this.transferTargets)
            {
                if (string.Equals(target.Extension, extension, StringComparison.Ordinal))
                    return target;
            }

            this.log.Info($"AI agent {this.agent.Number}: ignored a call to tool '{Flat(call.Name)}' for an extension it was not offered.");
            return null;
        }

        /// <summary>
        /// Whether a reply <i>promised</i> a transfer without calling the tool (D206). Deliberately
        /// narrow: a transfer-ish phrase, no negation, and no question mark — "I can't transfer you to
        /// Mark, but I can help" and "Is there anything else?" are not promises, and treating them as
        /// one would drop a live conversation into the handoff.
        /// </summary>
        public static bool LooksLikeTransferPromise(string text)
        {
            if (text.Length == 0)
                return false;

            // Models emit a curly apostrophe in "can’t" as often as the straight one, and a refusal
            // read as a promise would drop a live conversation into the handoff (D206).
            var flat = text.ReplaceLineEndings(" ").Replace('’', '\'').Trim();

            if (flat.EndsWith('?'))
                return false;

            if (Negations.Any(negation => flat.Contains(negation, StringComparison.OrdinalIgnoreCase)))
                return false;

            return Promises.Any(promise => flat.Contains(promise, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// A transcript line as one string: line breaks flattened to spaces, and a cap so one long
        /// reply cannot flood the log. The words are the caller's and the model's, not ours to tidy.
        /// </summary>
        private static string Flat(string text)
        {
            var flat = text.ReplaceLineEndings(" ").Trim();

            return flat.Length <= MaxLoggedText ? flat : flat[..MaxLoggedText] + "...";
        }

        /// <summary>How a line came out: how it ended, and how long the provider took to speak it.</summary>
        private readonly record struct Spoken(Playback Playback, long SpeechMs);

        private enum Playback
        {
            BargedIn,
            CallerGone,
            Finished,
        }
    }
}
