using System.Text;
using Techie.Pbx.Asterisk.Audio;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Turning a message on disk into something to attach, and into words (D129). Everything here
    /// fails open: what these tests are really checking is that none of the ways this can go wrong
    /// — no recording, no ffmpeg, ffmpeg refusing the file, no speech engine — throws at the
    /// caller or loses the email.
    ///
    /// The converter is a stand-in shell script rather than the real ffmpeg, so these run the same
    /// on a build agent that has never had ffmpeg installed.
    /// </summary>
    public class VoicemailRecordingTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-vmrec-").FullName;

        public void Dispose() => Directory.Delete(this.directory, recursive: true);

        [Fact]
        public void The_wav49_recording_is_what_it_reads()
        {
            var message = this.Message(".WAV", "RIFFnarrowband");
            File.WriteAllText(message + ".g722", "wideband");

            var recording = new VoicemailRecording(Missing(), Missing(), Missing());

            Assert.Equal(message + ".WAV", recording.SourcePath(message));
            Assert.Equal(Encoding.UTF8.GetBytes("RIFFnarrowband"), recording.Original(message));
        }

        /// <summary>A site that changed its formats still gets its recording attached.</summary>
        [Fact]
        public void The_wideband_copy_is_used_when_there_is_no_other()
        {
            var message = this.Message(".g722", "wideband");
            var recording = new VoicemailRecording(Missing(), Missing(), Missing());

            Assert.Equal(message + ".g722", recording.SourcePath(message));
        }

        /// <summary>
        /// "attach=no" is an ordinary setting and a message that has been moved on is an ordinary
        /// thing to find: no recording is not an error, it is an email without one.
        /// </summary>
        [Fact]
        public void A_message_with_no_recording_is_not_an_error()
        {
            var message = Path.Combine(this.directory, "msg0000");
            var recording = new VoicemailRecording(Missing(), Missing(), Missing());

            Assert.Null(recording.SourcePath(message));
            Assert.Null(recording.Original(message));
            Assert.Null(recording.Mp3(message));
            Assert.Null(recording.Transcript(message));
        }

        /// <summary>
        /// The sidecar is the email's source of truth (D129): what app_voicemail actually wrote —
        /// this is a real sidecar from the lab, with the "Name" &lt;number&gt; caller line, the
        /// duration and the arrival time — is what the email must say.
        /// </summary>
        [Fact]
        public void The_sidecar_says_who_called_and_how_long()
        {
            var message = this.Message(".WAV", "RIFFnarrowband");
            File.WriteAllText(message + ".txt", """
                ;
                ; Message Information file
                ;
                [message]
                origmailbox=103
                context=internal
                exten=103
                rdnis=unknown
                priority=4
                callerchan=PJSIP/102-0000001a
                callerid="Michael" <102>
                origdate=Tue Sep 22 05:24:47 AM UTC 2026
                origtime=1790054687
                category=
                msg_id=1790054687-00000006
                flag=
                duration=42
                """);

            var facts = VoicemailRecording.Facts(message);

            Assert.NotNull(facts);
            Assert.Equal("102", facts.CallerId);
            Assert.Equal("Michael", facts.CallerName);
            Assert.Equal(42, facts.DurationSeconds);
            Assert.Equal(1790054687, facts.ReceivedEpoch);
        }

        /// <summary>A missing sidecar is ordinary, not an error: the request's values stand.</summary>
        [Fact]
        public void A_message_without_a_sidecar_has_no_facts()
        {
            var message = this.Message(".WAV", "RIFFnarrowband");

            Assert.Null(VoicemailRecording.Facts(message));
        }

        /// <summary>
        /// The fallback the whole design rests on: a box without ffmpeg, or an ffmpeg that will
        /// not convert this file, produces no MP3 — and the caller attaches the original instead
        /// rather than failing the email.
        /// </summary>
        [Fact]
        public void No_ffmpeg_means_no_mp3_and_no_exception()
        {
            var message = this.Message(".WAV", "RIFFnarrowband");
            var recording = new VoicemailRecording(Missing(), Missing(), Missing());

            Assert.Null(recording.Mp3(message));
            Assert.NotNull(recording.Original(message));
        }

        [Fact]
        public void An_ffmpeg_that_refuses_the_file_means_no_mp3()
        {
            if (OperatingSystem.IsWindows())
                return;

            var message = this.Message(".WAV", "not really a recording");
            var recording = new VoicemailRecording(this.Program("refuse", "exit 3"), Missing(), Missing());

            Assert.Null(recording.Mp3(message));
        }

        [Fact]
        public void What_the_converter_wrote_is_what_is_attached()
        {
            if (OperatingSystem.IsWindows())
                return;

            var message = this.Message(".WAV", "RIFFnarrowband");
            var converter = this.Program("convert", "for last in \"$@\"; do :; done\nprintf 'ID3converted' > \"$last\"");
            var recording = new VoicemailRecording(converter, Missing(), Missing());

            Assert.Equal(Encoding.UTF8.GetBytes("ID3converted"), recording.Mp3(message));
        }

        /// <summary>
        /// A converter that exits 0 without writing anything — or writes an empty file — is not a
        /// conversion. Attaching nothing at all would be worse than attaching the original.
        /// </summary>
        [Fact]
        public void A_converter_that_produces_nothing_means_no_mp3()
        {
            if (OperatingSystem.IsWindows())
                return;

            var message = this.Message(".WAV", "RIFFnarrowband");
            var recording = new VoicemailRecording(this.Program("silent", "exit 0"), Missing(), Missing());

            Assert.Null(recording.Mp3(message));
        }

        /// <summary>
        /// Transcription is an optional part of the install (D128). A box without the engine or
        /// without the model transcribes nothing, and says nothing about it to the caller.
        /// </summary>
        [Fact]
        public void No_speech_engine_means_no_transcript()
        {
            if (OperatingSystem.IsWindows())
                return;

            var message = this.Message(".WAV", "RIFFnarrowband");
            var converter = this.Program("convert", "for last in \"$@\"; do :; done\nprintf 'wav' > \"$last\"");

            Assert.Null(new VoicemailRecording(converter, Missing(), Missing()).Transcript(message));
        }

        [Fact]
        public void The_transcript_is_what_the_engine_said()
        {
            if (OperatingSystem.IsWindows())
                return;

            var message = this.Message(".WAV", "RIFFnarrowband");
            var converter = this.Program("convert", "for last in \"$@\"; do :; done\nprintf 'wav' > \"$last\"");
            var engine = this.Program("whisper", "printf ' Hello, it is Jo. \\n\\n Call me back. \\n'");
            var model = this.Write("ggml-small.en.bin", "not really a model");

            var transcript = new VoicemailRecording(converter, engine, model).Transcript(message);

            Assert.Equal("Hello, it is Jo. Call me back.", transcript);
        }

        [Fact]
        public void An_engine_that_hears_nothing_is_no_transcript()
        {
            if (OperatingSystem.IsWindows())
                return;

            var message = this.Message(".WAV", "RIFFnarrowband");
            var converter = this.Program("convert", "for last in \"$@\"; do :; done\nprintf 'wav' > \"$last\"");
            var engine = this.Program("quiet", "printf '\\n  \\n'");
            var model = this.Write("ggml-small.en.bin", "not really a model");

            Assert.Null(new VoicemailRecording(converter, engine, model).Transcript(message));
        }

        /// <summary>A path nothing is at, which is what "not installed" looks like from here.</summary>
        private static string Missing() => "/nonexistent/tnpbx-not-installed";

        /// <summary>One message's file on disk, and the path to it without the extension.</summary>
        private string Message(string extension, string contents)
        {
            this.Write("msg0000" + extension, contents);

            return Path.Combine(this.directory, "msg0000");
        }

        /// <summary>
        /// A stand-in for ffmpeg or whisper: a shell script that does one thing, so the tests can
        /// say what happens when the real program succeeds, fails or says nothing without either
        /// of them being installed.
        /// </summary>
        private string Program(string name, string body)
        {
            var path = Path.Combine(this.directory, name);

            File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            return path;
        }

        /// <summary>One file in this test's own directory, and the path to it.</summary>
        private string Write(string name, string contents)
        {
            var path = Path.Combine(this.directory, name);
            File.WriteAllText(path, contents);

            return path;
        }
    }
}
