namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// Decides when a caller has finished speaking (D200), by energy alone: 16-bit PCM at 8 kHz is
    /// cut into 20 ms windows, a window whose RMS reaches <see cref="SpeechRmsThreshold"/> is
    /// speech, and a turn ends after <see cref="TrailingSilenceMs"/> of quiet following at least
    /// <see cref="MinSpeechMs"/> of speech — or at <see cref="MaxUtteranceMs"/>, however much
    /// the caller is still saying. A burst shorter than the minimum is a cough or a click and is
    /// forgotten.
    ///
    /// The last <see cref="PreRollMs"/> before speech starts is kept and handed back with the
    /// utterance, so the quiet first consonant that did not reach the threshold is not clipped.
    /// Pure: no timers, no clock, only the samples it is given.
    /// </summary>
    public class Endpointer
    {
        /// <summary>The longest single utterance; a turn is cut here even mid-sentence.</summary>
        public const int MaxUtteranceMs = 20_000;

        /// <summary>How much speech a turn needs before silence can end it.</summary>
        public const int MinSpeechMs = 300;

        /// <summary>How much audio before the first loud window is kept with the utterance.</summary>
        public const int PreRollMs = 300;

        public const int SampleRateHz = 8000;

        /// <summary>
        /// The RMS, in 16-bit sample units, at which a window counts as speech. Line noise on a
        /// telephone call sits well below it; ordinary speech is several times above it.
        /// </summary>
        public const double SpeechRmsThreshold = 500;

        /// <summary>The quiet that ends a turn once enough speech has been heard.</summary>
        public const int TrailingSilenceMs = 400;

        /// <summary>The analysis window: 20 ms, the size of one AudioSocket frame.</summary>
        public const int WindowMs = 20;

        public const int WindowSamples = SampleRateHz * WindowMs / 1000;

        private const int MaxUtteranceSamples = SampleRateHz / 1000 * MaxUtteranceMs;
        private const int PreRollWindows = PreRollMs / WindowMs;

        private readonly Queue<short[]> preRoll = new();
        private readonly List<short> utterance = new();
        private readonly short[] window = new short[WindowSamples];

        private bool complete;
        private bool speaking;
        private int silenceMs;
        private int speechMs;
        private int windowFill;

        /// <summary>Whether a turn has been completed and is waiting for <see cref="Take"/>.</summary>
        public bool IsComplete => this.complete;

        /// <summary>Whether speech has started and the turn is still open.</summary>
        public bool IsSpeaking => this.speaking;

        /// <summary>Whether a block of samples is loud enough to count as speech.</summary>
        public static bool IsSpeech(ReadOnlySpan<short> samples) => Rms(samples) >= SpeechRmsThreshold;

        /// <summary>The root-mean-square of the samples, or 0 for none.</summary>
        public static double Rms(ReadOnlySpan<short> samples)
        {
            if (samples.Length == 0)
                return 0;

            double sum = 0;
            foreach (var sample in samples)
                sum += (double)sample * sample;

            return Math.Sqrt(sum / samples.Length);
        }

        /// <summary>
        /// Feeds samples in; returns true once the turn is complete. Samples after the end of a
        /// turn are not kept, and it goes on returning true until <see cref="Take"/> or <see cref="Reset"/>.
        /// </summary>
        public bool Add(ReadOnlySpan<short> samples)
        {
            foreach (var sample in samples)
            {
                if (this.complete)
                    break;

                this.window[this.windowFill++] = sample;

                if (this.windowFill == WindowSamples)
                {
                    this.windowFill = 0;
                    this.AddWindow(this.window);
                }
            }

            return this.complete;
        }

        /// <summary>Forgets everything: no turn, no pre-roll.</summary>
        public void Reset()
        {
            this.complete = false;
            this.preRoll.Clear();
            this.silenceMs = 0;
            this.speaking = false;
            this.speechMs = 0;
            this.utterance.Clear();
            this.windowFill = 0;
        }

        /// <summary>The utterance, pre-roll first, and a fresh start for the next turn.</summary>
        public short[] Take()
        {
            var taken = this.utterance.ToArray();
            this.Reset();
            return taken;
        }

        private void AddWindow(short[] samples)
        {
            var loud = IsSpeech(samples);

            if (!this.speaking)
            {
                if (!loud)
                {
                    this.preRoll.Enqueue((short[])samples.Clone());
                    if (this.preRoll.Count > PreRollWindows)
                        this.preRoll.Dequeue();
                    return;
                }

                this.speaking = true;
                this.silenceMs = 0;
                this.speechMs = 0;
                foreach (var earlier in this.preRoll)
                    this.utterance.AddRange(earlier);
                this.preRoll.Clear();
            }

            this.utterance.AddRange(samples);

            if (loud)
            {
                this.speechMs += WindowMs;
                this.silenceMs = 0;
            }
            else
            {
                this.silenceMs += WindowMs;
            }

            if (this.utterance.Count >= MaxUtteranceSamples)
            {
                this.complete = true;
            }
            else if (this.silenceMs >= TrailingSilenceMs)
            {
                if (this.speechMs >= MinSpeechMs)
                    this.complete = true;
                else
                    this.ForgetBurst();
            }
        }

        /// <summary>Too little speech to be a turn: back to listening, with no utterance.</summary>
        private void ForgetBurst()
        {
            this.silenceMs = 0;
            this.speaking = false;
            this.speechMs = 0;
            this.utterance.Clear();
        }
    }
}
