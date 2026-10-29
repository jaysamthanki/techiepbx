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
    /// The network is three delegates, so the whole turn loop is testable with fakes. Nothing the
    /// caller says and nothing the model replies is ever logged.
    /// </summary>
    public class AiCallSession
    {
        /// <summary>How many earlier messages (caller and agent together) go with each chat request.</summary>
        public const int HistoryMessages = 12;

        /// <summary>Turns in a row with nothing transcribed before the agent gives up on the call.</summary>
        public const int MaxSilentTurns = 3;

        /// <summary>The token the model writes to ask for a person. Stripped before the reply is spoken.</summary>
        public const string TransferToken = "[TRANSFER]";

        /// <summary>The longest call an agent will hold; then the socket is closed and the dialplan carries on.</summary>
        public static readonly TimeSpan MaxCallDuration = TimeSpan.FromMinutes(10);

        /// <summary>How much of one turn's words a transcript line carries, so one long reply cannot flood the log.</summary>
        public const int MaxLoggedText = 500;

        private readonly AiAgent agent;
        private readonly Func<IReadOnlyList<(string Role, string Content)>, string, CancellationToken, Task<string>> chat;
        private readonly Endpointer endpointer = new();
        private readonly Channel<AudioSocketFrame> frames = Channel.CreateUnbounded<AudioSocketFrame>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        private readonly List<(string Role, string Content)> history = new();
        private readonly ILog log;

        private readonly List<string> transcript = new();
        private readonly AiSettingsSnapshot settings;
        private readonly Func<string, string, string, CancellationToken, Task<byte[]>> speak;
        private readonly Func<byte[], CancellationToken, Task<string>> transcribe;

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
        /// μ-law 8 kHz bytes; <paramref name="chat"/> takes the messages and the model slug.
        /// </summary>
        public AiCallSession(
            AiAgent agent,
            AiSettingsSnapshot settings,
            ILog log,
            Func<byte[], CancellationToken, Task<string>> transcribe,
            Func<IReadOnlyList<(string Role, string Content)>, string, CancellationToken, Task<string>> chat,
            Func<string, string, string, CancellationToken, Task<byte[]>> speak)
        {
            this.agent = agent;
            this.chat = chat;
            this.log = log;
            this.settings = settings;
            this.speak = speak;
            this.transcribe = transcribe;
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
        /// The system prompt the model sees: the agent's own, and — only when the agent has
        /// somewhere to hand a caller — the rule for asking for that.
        /// </summary>
        public static string SystemPrompt(AiAgent agent)
        {
            if (agent.TransferTo.Length == 0)
                return agent.Prompt;

            return agent.Prompt.TrimEnd() + "\n\n" +
                $"If the caller asks for a person, or you cannot help them, a transfer to a member of staff is available: " +
                $"say a short goodbye and end your reply with the exact token {TransferToken} on its own.";
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
            var model = this.agent.Model.Length > 0 ? this.agent.Model : this.settings.OpenRouterModel;
            var silentTurns = 0;
            var turn = 0;
            var systemPrompt = SystemPrompt(this.agent);

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
                var reply = await this.chat(messages, model, cancellationToken);
                var chatMs = clock.ElapsedMilliseconds;
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
