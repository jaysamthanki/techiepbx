using System.Text;
using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Controllers;

namespace Techie.Pbx.Tests.Web
{
    /// <summary>
    /// The notify endpoint's two decisions about where a recording comes from (D162): the bytes
    /// posted in the request are preferred, because a mailbox with delete=yes has no spool files
    /// left by the time the application looks, and the spool path is only read when there are no
    /// posted bytes. The decoding and the size cap have their own tests beside
    /// <c>VoicemailRecording</c>; these prove the order of preference.
    ///
    /// As in <c>VoicemailRecordingTests</c>, ffmpeg and whisper are stand-in shell scripts, so
    /// these run the same on a machine that has neither.
    /// </summary>
    public class VoicemailNotifyControllerTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-vmnotify-").FullName;

        public void Dispose() => Directory.Delete(this.directory, recursive: true);

        /// <summary>
        /// The race D162 exists for: the spool file is there with different bytes (delete=no), but
        /// the posted recording is what gets attached — here proven with a converter that refuses,
        /// so the attachment is the original, and the original is the posted bytes.
        /// </summary>
        [Fact]
        public void The_posted_recording_wins_over_the_spool()
        {
            if (OperatingSystem.IsWindows())
                return;

            var message = this.Message("RIFFspool-copy");
            var recording = new VoicemailRecording(this.Program("refuse", "exit 3"), Missing(), Missing());
            var posted = Encoding.UTF8.GetBytes("RIFFposted-copy");

            var attachment = VoicemailNotifyController.Attachment(
                Mailbox(), recording, posted, "wav", message, DateTimeOffset.Now);

            Assert.NotNull(attachment);
            Assert.Equal(posted, attachment.Bytes);
        }

        [Fact]
        public void The_posted_recording_is_attached_as_an_mp3_when_ffmpeg_can()
        {
            if (OperatingSystem.IsWindows())
                return;

            var converter = this.Program("convert", "for last in \"$@\"; do :; done\nprintf 'ID3converted' > \"$last\"");
            var recording = new VoicemailRecording(converter, Missing(), Missing());

            var attachment = VoicemailNotifyController.Attachment(
                Mailbox(), recording, Encoding.UTF8.GetBytes("RIFFposted"), "WAV", "", DateTimeOffset.Now);

            Assert.NotNull(attachment);
            Assert.Equal(Encoding.UTF8.GetBytes("ID3converted"), attachment.Bytes);
            Assert.EndsWith(".mp3", attachment.FileName);
        }

        /// <summary>An older script sends only a path, and everything works as it did (D129).</summary>
        [Fact]
        public void Without_posted_bytes_the_spool_is_read_as_before()
        {
            var message = this.Message("RIFFspool-copy");
            var recording = new VoicemailRecording(Missing(), Missing(), Missing());

            var attachment = VoicemailNotifyController.Attachment(
                Mailbox(), recording, null, null, message, DateTimeOffset.Now);

            Assert.NotNull(attachment);
            Assert.Equal(Encoding.UTF8.GetBytes("RIFFspool-copy"), attachment.Bytes);
        }

        /// <summary>Neither posted nor on disk is an email without audio, exactly as today.</summary>
        [Fact]
        public void No_recording_anywhere_is_no_attachment_and_no_exception()
        {
            var recording = new VoicemailRecording(Missing(), Missing(), Missing());

            Assert.Null(VoicemailNotifyController.Attachment(
                Mailbox(), recording, null, null, "", DateTimeOffset.Now));
        }

        /// <summary>attach=no still means what it asked for, whatever the script posted.</summary>
        [Fact]
        public void A_mailbox_that_asked_for_no_audio_gets_none()
        {
            var mailbox = Mailbox();
            mailbox.VoicemailAttachRecording = false;

            var recording = new VoicemailRecording(Missing(), Missing(), Missing());

            Assert.Null(VoicemailNotifyController.Attachment(
                mailbox, recording, Encoding.UTF8.GetBytes("RIFFposted"), "wav", "", DateTimeOffset.Now));
        }

        /// <summary>
        /// The transcript comes out of the posted bytes when they are there — here proven with a
        /// path that has no files at all, which is exactly what delete=yes leaves behind.
        /// </summary>
        [Fact]
        public void The_transcript_is_read_from_the_posted_recording()
        {
            if (OperatingSystem.IsWindows())
                return;

            var converter = this.Program("convert", "for last in \"$@\"; do :; done\nprintf 'wav' > \"$last\"");
            var engine = this.Program("whisper", "printf 'Call me back.'");
            var model = this.Write("ggml-small.en.bin", "not really a model");
            var mailbox = Mailbox();
            mailbox.VoicemailTranscribe = true;

            var transcript = VoicemailNotifyController.Transcript(
                mailbox, new VoicemailRecording(converter, engine, model),
                Encoding.UTF8.GetBytes("RIFFposted"), "wav", Path.Combine(this.directory, "gone"));

            Assert.Equal("Call me back.", transcript);
        }

        /// <summary>A mailbox that did not ask must not pay for the attempt (D128).</summary>
        [Fact]
        public void A_mailbox_that_did_not_ask_gets_no_transcript()
        {
            if (OperatingSystem.IsWindows())
                return;

            var converter = this.Program("convert", "for last in \"$@\"; do :; done\nprintf 'wav' > \"$last\"");
            var engine = this.Program("whisper", "printf 'Call me back.'");
            var model = this.Write("ggml-small.en.bin", "not really a model");

            Assert.Null(VoicemailNotifyController.Transcript(
                Mailbox(), new VoicemailRecording(converter, engine, model),
                Encoding.UTF8.GetBytes("RIFFposted"), "wav", ""));
        }

        /// <summary>An extension whose mailbox attaches audio, as most do.</summary>
        private static Extension Mailbox() => new()
        {
            Number = "201",
            VoicemailAttachRecording = true,
            VoicemailEnabled = true,
        };

        /// <summary>A path nothing is at, which is what "not installed" looks like from here.</summary>
        private static string Missing() => "/nonexistent/tnpbx-not-installed";

        /// <summary>One message's recording on disk, and the path to it without the extension.</summary>
        private string Message(string contents)
        {
            this.Write("msg0000.WAV", contents);

            return Path.Combine(this.directory, "msg0000");
        }

        /// <summary>
        /// A stand-in for ffmpeg or whisper, exactly as <c>VoicemailRecordingTests</c> makes them.
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
