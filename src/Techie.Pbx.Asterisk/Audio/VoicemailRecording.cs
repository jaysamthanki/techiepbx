using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using log4net;

namespace Techie.Pbx.Asterisk.Audio
{
    /// <summary>
    /// What this application does with a voicemail recording on its way into an email (D129): turn
    /// it into an MP3 anybody's phone will play, and — when the mailbox asked for one — read the
    /// words out of it with whisper.cpp on this same box (D128).
    ///
    /// Everything here fails open and returns null. A recording that cannot be converted is still
    /// a voicemail somebody has to hear, so the caller attaches the original instead; a transcript
    /// that cannot be produced is simply not in the email. Nothing here throws, and nothing here
    /// is worth losing a message over.
    ///
    /// ffmpeg and whisper-cli are started with a fixed argv list — never a shell string, never a
    /// command built by concatenation — and the only path they are ever handed is one this class
    /// or <see cref="Config.VoicemailSpool"/> built (security.md).
    /// </summary>
    public class VoicemailRecording
    {
        /// <summary>The program name, resolved on PATH, exactly as <see cref="AudioConverter"/> does.</summary>
        public const string DefaultFfmpegProgram = "ffmpeg";

        /// <summary>The model install.sh downloads beside the engine; absent on a box built without it (D128).</summary>
        public const string DefaultWhisperModel = "/opt/tnpbx/whisper/ggml-small.en.bin";

        /// <summary>whisper.cpp, built by install.sh. Optional: a box without it simply does not transcribe.</summary>
        public const string DefaultWhisperProgram = "/opt/tnpbx/bin/whisper-cli";

        /// <summary>
        /// The MP3 bitrate. 48 kbit/s mono is generous for 8 kHz telephone speech and keeps a
        /// five minute message under two megabytes, which no mailbox will refuse.
        /// </summary>
        public const int Mp3BitrateKbps = 48;

        /// <summary>
        /// Converting a message is seconds of work on a file capped at five minutes; anything near
        /// this is a wedged process rather than a slow one.
        /// </summary>
        private const int ConvertTimeoutSeconds = 60;

        /// <summary>
        /// Bigger than any voicemail this system records, and the point at which something is
        /// wrong rather than long. Nothing this size is attached to an email.
        /// </summary>
        private const int MaxAudioBytes = 8 * 1024 * 1024;

        /// <summary>
        /// small.en is roughly real time and voicemail.conf caps a message at 300 seconds, so this
        /// is where something has gone wrong rather than where a long message gives up (D128).
        /// </summary>
        private const int TranscribeTimeoutSeconds = 300;

        /// <summary>
        /// The recordings app_voicemail writes, in the order they are worth reading: wav49 first
        /// because that is the format voicemail.conf lists first (D126), then plain PCM WAV for a
        /// site that changed it, then the wideband copy.
        /// </summary>
        private static readonly string[] AudioExtensions = { ".WAV", ".wav", ".g722" };

        private static readonly ILog Log = LogManager.GetLogger(typeof(VoicemailRecording));

        /// <summary>The ffmpeg binary to run. A parameter so a test can point it somewhere else.</summary>
        public string FfmpegProgram { get; }

        public string WhisperModelPath { get; }

        public string WhisperProgram { get; }

        public VoicemailRecording()
            : this(DefaultFfmpegProgram, DefaultWhisperProgram, DefaultWhisperModel)
        {
        }

        public VoicemailRecording(string ffmpegProgram, string whisperProgram, string whisperModelPath)
        {
            this.FfmpegProgram = ffmpegProgram;
            this.WhisperModelPath = whisperModelPath;
            this.WhisperProgram = whisperProgram;
        }

        /// <summary>
        /// The recording as an MP3, or null if there is nothing to convert or ffmpeg would not
        /// convert it. Null is not a failure the caller has to report: it attaches
        /// <see cref="Original"/> instead, and the email goes out either way.
        /// </summary>
        public byte[]? Mp3(string messagePath)
        {
            var source = this.SourcePath(messagePath);

            if (source == null)
                return null;

            // Ours, and gone before this returns however it returns. The recording is a customer's
            // voicemail: it does not linger in /tmp (D128).
            var workspace = Workspace();

            if (workspace == null)
                return null;

            try
            {
                var target = Path.Combine(workspace, "message.mp3");

                if (!this.Run(this.FfmpegProgram, Mp3Arguments(source, target), ConvertTimeoutSeconds, out _))
                    return null;

                return Read(target);
            }
            finally
            {
                Remove(workspace);
            }
        }

        /// <summary>
        /// The recording exactly as Asterisk wrote it, for the email that could not have an MP3.
        /// Null when the message has no recording on disk at all.
        /// </summary>
        public byte[]? Original(string messagePath)
        {
            var source = this.SourcePath(messagePath);

            return source == null ? null : Read(source);
        }

        /// <summary>
        /// Which file on disk holds this message's audio, or null when none of them does — a
        /// mailbox set to record no audio, or a message that has already been moved on.
        /// </summary>
        public string? SourcePath(string messagePath)
        {
            foreach (var extension in AudioExtensions)
            {
                var path = messagePath + extension;

                if (Exists(path))
                    return path;
            }

            Log.Info($"No recording beside {messagePath}, so the email carries no audio");
            return null;
        }

        /// <summary>
        /// What the caller said, or null for every way that can not happen: no recording, whisper
        /// or its model not installed, ffmpeg missing, either one failing or taking too long, or
        /// no speech in the message. Every one of those is an ordinary outcome (D128).
        /// </summary>
        public string? Transcript(string messagePath)
        {
            var source = this.SourcePath(messagePath);

            if (source == null)
                return null;

            if (!Exists(this.WhisperProgram) || !Exists(this.WhisperModelPath))
            {
                Log.Info($"No speech recognition on this box ({this.WhisperProgram}, {this.WhisperModelPath}), so the email carries no transcript");
                return null;
            }

            var workspace = Workspace();

            if (workspace == null)
                return null;

            var started = Stopwatch.StartNew();

            try
            {
                // whisper takes 16 kHz mono PCM and nothing else, and what is on disk is 8 kHz
                // GSM-in-WAV, so ffmpeg converts it first — the same step the script used to do.
                var wideband = Path.Combine(workspace, "message-16k.wav");

                if (!this.Run(this.FfmpegProgram, ResampleArguments(source, wideband), ConvertTimeoutSeconds, out _))
                    return null;

                if (!this.Run(this.WhisperProgram, this.WhisperArguments(wideband), TranscribeTimeoutSeconds, out var spoken))
                    return null;

                var transcript = string.Join(" ", spoken
                    .ReplaceLineEndings("\n")
                    .Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0));

                if (transcript.Length == 0)
                {
                    Log.Info($"whisper found no speech in {messagePath} ({started.Elapsed.TotalSeconds:0.0}s)");
                    return null;
                }

                // The length, never the words: a transcript is the message itself, and the message
                // does not belong in a log file.
                Log.Info($"Transcribed {messagePath} in {started.Elapsed.TotalSeconds:0.0}s ({transcript.Length} characters)");

                return transcript;
            }
            finally
            {
                Remove(workspace);
            }
        }

        private static bool Exists(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not look at {path}: {ex.Message}");
                return false;
            }
        }

        private static void Kill(Process process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                Log.Warn($"Could not stop {process.ProcessName}: {ex.Message}");
            }
        }

        /// <summary>
        /// The fixed argv for the attachment: mono MP3, no video stream from a source that should
        /// not have one, and no metadata carried into a file somebody downloads.
        /// </summary>
        private static List<string> Mp3Arguments(string sourcePath, string targetPath) => new()
        {
            "-nostdin",
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-i", sourcePath,
            "-vn",
            "-map_metadata", "-1",
            "-ac", "1",
            "-codec:a", "libmp3lame",
            "-b:a", Mp3BitrateKbps.ToString(CultureInfo.InvariantCulture) + "k",
            targetPath,
        };

        /// <summary>The file, or null when it is missing, unreadable or absurdly large.</summary>
        private static byte[]? Read(string path)
        {
            try
            {
                var length = new FileInfo(path).Length;

                if (length == 0)
                    return null;

                if (length > MaxAudioBytes)
                {
                    Log.Warn($"{path} is {length} bytes, which is larger than any voicemail this system records; not attaching it");
                    return null;
                }

                return File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not read {path}: {ex.Message}");
                return null;
            }
        }

        private static void Remove(string workspace)
        {
            try
            {
                Directory.Delete(workspace, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not clear {workspace}: {ex.Message}");
            }
        }

        /// <summary>The fixed argv for whisper's input: 16 kHz mono PCM, which is all it takes.</summary>
        private static List<string> ResampleArguments(string sourcePath, string targetPath) => new()
        {
            "-nostdin",
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-i", sourcePath,
            "-vn",
            "-ar", "16000",
            "-ac", "1",
            "-c:a", "pcm_s16le",
            targetPath,
        };

        /// <summary>
        /// Runs one of the two programs and says whether it worked, with whatever it wrote to
        /// standard output. Never throws: "not installed" is a state this box is allowed to be in.
        /// </summary>
        private bool Run(string program, List<string> arguments, int timeoutSeconds, out string output)
        {
            output = "";

            var start = new ProcessStartInfo
            {
                FileName = program,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);

            Process? process;

            try
            {
                process = Process.Start(start);
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
            {
                Log.Warn($"Could not run '{program}': {ex.Message}");
                return false;
            }

            if (process == null)
            {
                Log.Warn($"Could not run '{program}'");
                return false;
            }

            using (process)
            {
                // Read the pipes while it runs: a process that fills one and blocks would
                // otherwise never reach the exit we are waiting for.
                var error = process.StandardError.ReadToEndAsync();
                var standard = process.StandardOutput.ReadToEndAsync();

                if (!process.WaitForExit(timeoutSeconds * 1000))
                {
                    Kill(process);
                    Log.Warn($"'{program}' took longer than {timeoutSeconds} seconds and was stopped");
                    return false;
                }

                // The overload without a timeout is what flushes the redirected pipes.
                process.WaitForExit();
                Task.WaitAll(error, standard);

                if (process.ExitCode != 0)
                {
                    Log.Warn($"'{program}' exited {process.ExitCode}: {Trimmed(error.Result)}");
                    return false;
                }

                output = standard.Result;
                return true;
            }
        }

        /// <summary>These two can be wordy; the log wants the gist, not a screen of it.</summary>
        private static string Trimmed(string error)
        {
            var text = error.ReplaceLineEndings(" ").Trim();
            return text.Length <= 500 ? text : text[..500] + "…";
        }

        /// <summary>
        /// The fixed argv for whisper. "-nt" leaves the timestamps out, because this goes into an
        /// email rather than a subtitle file, and "-np" keeps its own banner out of the output.
        /// </summary>
        private List<string> WhisperArguments(string wavPath) => new()
        {
            "-m", this.WhisperModelPath,
            "-f", wavPath,
            "-nt",
            "-np",
        };

        /// <summary>
        /// A temporary directory of our own, or null when even that will not work. Mode 0700 by
        /// the framework's own doing, which is what keeps a customer's message off the rest of the
        /// machine while it is being converted.
        /// </summary>
        private static string? Workspace()
        {
            try
            {
                return Directory.CreateTempSubdirectory("tnpbx-vm-").FullName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not make a temporary directory to convert a voicemail in: {ex.Message}");
                return null;
            }
        }
    }
}
