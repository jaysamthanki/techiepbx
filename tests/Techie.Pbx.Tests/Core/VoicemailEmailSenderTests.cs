using System.Text;
using Techie.Pbx.Core.Mail;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// What goes on the message besides the body (D129). Sending itself needs a relay and is not
    /// tested here — <see cref="MailSenderTests"/> covers what happens without one — so what is
    /// worth pinning down is the attachment: its name is ours rather than anything a caller said,
    /// and its type tells the recipient's mail client whether it can play it.
    /// </summary>
    public class VoicemailEmailSenderTests
    {
        [Fact]
        public void An_mp3_attachment_is_named_after_the_mailbox_and_the_time()
        {
            var attachment = VoicemailEmailSender.Attachment(
                "201",
                new DateTimeOffset(2026, 9, 21, 9, 14, 3, TimeSpan.FromHours(1)),
                Audio(),
                isMp3: true);

            Assert.Equal("voicemail-201-20260921-091403.mp3", attachment.FileName);
            Assert.Equal(VoicemailEmailSender.Mp3ContentType, attachment.ContentType);
            Assert.Equal(Audio(), attachment.Bytes);
        }

        /// <summary>
        /// The email ffmpeg could not help with. The recording still goes, as Asterisk wrote it,
        /// and it is labelled honestly rather than called an MP3.
        /// </summary>
        [Fact]
        public void The_original_recording_is_attached_as_a_wav()
        {
            var attachment = VoicemailEmailSender.Attachment(
                "201",
                new DateTimeOffset(2026, 9, 21, 9, 14, 3, TimeSpan.Zero),
                Audio(),
                isMp3: false);

            Assert.Equal("voicemail-201-20260921-091403.wav", attachment.FileName);
            Assert.Equal(VoicemailEmailSender.WavContentType, attachment.ContentType);
        }

        /// <summary>
        /// The time in the name is the local time on the label, not UTC: two messages an hour
        /// apart must not sort into the same name, and an admin reading a folder of them should
        /// see the time the phone rang.
        /// </summary>
        [Fact]
        public void The_name_uses_the_time_as_it_was_given()
        {
            var attachment = VoicemailEmailSender.Attachment(
                "9999",
                new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5)),
                Audio(),
                isMp3: true);

            Assert.Equal("voicemail-9999-20260102-030405.mp3", attachment.FileName);
        }

        private static byte[] Audio() => Encoding.ASCII.GetBytes("not really an mp3");
    }
}
