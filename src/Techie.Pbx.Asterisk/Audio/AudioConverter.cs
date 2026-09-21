using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using log4net;

namespace Techie.Pbx.Asterisk.Audio
{
    /// <summary>
    /// Turns whatever was uploaded into the one format this system stores: 16-bit 8 kHz mono PCM
    /// WAV, which is what Asterisk's <c>format_wav</c> plays with no transcoding at call time
    /// (D55).
    ///
    /// ffmpeg is a required system dependency, run as the unprivileged web user. The arguments are
    /// a fixed argv list — never a shell string, never a command built by concatenation — so a
    /// file name cannot become an argument and an argument cannot become a command
    /// (security.md: "never shells out with string-built commands").
    /// </summary>
    /// <summary>
    /// One stored-audio target: the file extension, the sample rate ffmpeg writes, and the codec
    /// and muxer it uses. Two exist (D55 announcements, D122 hold music); a third would be added
    /// here, not by widening a converter with a mode flag.
    /// </summary>
    public sealed record AudioOutput(
        string Extension,
        int SampleRateHz,
        string Codec,
        string Muxer);

    public class AudioConverter
    {
        /// <summary>Mono: a phone call is one channel, and stereo would only be thrown away.</summary>
        public const int Channels = 1;

        /// <summary>8 kHz, the sample rate a narrowband SIP call actually carries.</summary>
        public const int SampleRateHz = 8000;

        /// <summary>The program name, resolved on PATH. Debian's <c>ffmpeg</c> package installs it.</summary>
        public const string DefaultProgram = "ffmpeg";

        /// <summary>
        /// Long enough for a 20 MB video to be decoded on a small VM, short enough that a wedged
        /// process does not hold a request open all day.
        /// </summary>
        private const int DefaultTimeoutSeconds = 120;

        private static readonly ILog Log = LogManager.GetLogger(typeof(AudioConverter));

        /// <summary>The ffmpeg binary to run. A parameter so a test can point it somewhere else.</summary>
        public string Program { get; }

        public int TimeoutSeconds { get; }

        /// <summary>What to write: the sample rate, codec and muxer the stored file gets.</summary>
        public AudioOutput Output { get; }

        /// <summary>
        /// The announcement format, 8 kHz mono PCM WAV (D55): what a prompt plays from, and the
        /// default so the announcement stores keep their behaviour unchanged.
        /// </summary>
        public static AudioOutput PromptWav { get; } = new(".wav", 8000, "pcm_s16le", "wav");

        /// <summary>
        /// The music on hold format, 16 kHz mono G.722 (D122): wideband, the same as the voice
        /// path (D117), so a G.722 call plays it with no transcoding at all.
        /// </summary>
        public static AudioOutput MohG722 { get; } = new(".g722", 16000, "g722", "g722");

        public AudioConverter()
            : this(DefaultProgram, DefaultTimeoutSeconds, PromptWav)
        {
        }

        public AudioConverter(string program, int timeoutSeconds, AudioOutput? output = null)
        {
            this.Program = program;
            this.TimeoutSeconds = timeoutSeconds;
            this.Output = output ?? PromptWav;
        }

        /// <summary>
        /// Converts one file into the stored format, overwriting the target. Throws
        /// <see cref="AudioUploadException"/> for everything the admin has to be told about,
        /// including ffmpeg not being installed — an upload never falls back to storing the file
        /// as it arrived.
        /// </summary>
        public void ToPrompt(string sourcePath, string targetPath)
        {
            var start = new ProcessStartInfo
            {
                FileName = this.Program,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            foreach (var argument in Arguments(sourcePath, targetPath))
                start.ArgumentList.Add(argument);

            using var process = Start(start);

            // Read the pipes while it runs: a process that fills one and blocks would otherwise
            // never reach the exit we are waiting for.
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(this.TimeoutSeconds * 1000))
            {
                Kill(process);
                throw new AudioUploadException(
                    $"Converting the audio took longer than {this.TimeoutSeconds} seconds and was stopped. Try a shorter recording.");
            }

            // The overload without a timeout is what flushes the redirected pipes.
            process.WaitForExit();
            Task.WaitAll(error, output);

            if (process.ExitCode == 0)
                return;

            Log.Error($"{this.Program} exited {process.ExitCode} converting an upload: {Trimmed(error.Result)}");
            throw new AudioUploadException(
                "That file could not be converted to the format Asterisk plays. It may be damaged, or it may contain no audio.");
        }

        /// <summary>
        /// The fixed argv. Everything is a separate element, so neither path is ever parsed as
        /// anything but a file name.
        /// </summary>
        private List<string> Arguments(string sourcePath, string targetPath) => new()
        {
            "-nostdin",                 // never wait on a console that isn't there
            "-hide_banner",
            "-loglevel", "error",
            "-y",                       // the target is ours and already resolved
            "-i", sourcePath,
            "-vn",                      // an iPhone clip carries video; a prompt does not
            "-map_metadata", "-1",      // no tags in the file we store
            "-ac", Channels.ToString(CultureInfo.InvariantCulture),
            "-ar", this.Output.SampleRateHz.ToString(CultureInfo.InvariantCulture),
            "-acodec", this.Output.Codec,
            "-f", this.Output.Muxer,
            targetPath,
        };

        private static void Kill(Process process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                Log.Warn($"Could not stop the conversion process: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts ffmpeg, turning "it is not installed" into a message that says so by name. The
        /// alternative — storing the file unconverted — would leave Asterisk unable to play it,
        /// which is a fault that only shows up on a real call.
        /// </summary>
        private Process Start(ProcessStartInfo start)
        {
            try
            {
                return Process.Start(start) ?? throw new AudioUploadException(NotInstalled());
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
            {
                Log.Error($"Could not run '{this.Program}': {ex.Message}");
                throw new AudioUploadException(NotInstalled(), ex);
            }
        }

        private string NotInstalled() =>
            $"ffmpeg is not installed on this server, so uploaded audio cannot be converted. " +
            $"Install it with 'apt install ffmpeg' (the command '{this.Program}' has to be on the service's PATH) and try again.";

        /// <summary>ffmpeg can be wordy; the log wants the gist, not a screen of it.</summary>
        private static string Trimmed(string error)
        {
            var text = error.ReplaceLineEndings(" ").Trim();
            return text.Length <= 500 ? text : text[..500] + "…";
        }
    }
}
