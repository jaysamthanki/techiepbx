using Techie.Pbx.Core.Mail;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Filling in the D129 template. The same kind of tests as the alert renderer's (D114): data
    /// in, text out, and the things that must never appear in the output — with one difference
    /// that matters here. An alert's values are ours; a voicemail's caller ID name is a string a
    /// stranger on the telephone network chose, so the encoding tests are the point of this file
    /// rather than a formality.
    /// </summary>
    public class VoicemailEmailRendererTests
    {
        [Fact]
        public void Nothing_is_left_unfilled()
        {
            var html = VoicemailEmailRenderer.Render(Voicemail());

            Assert.DoesNotContain("{{", html);
        }

        [Fact]
        public void The_values_it_was_given_are_in_the_output()
        {
            var html = VoicemailEmailRenderer.Render(Voicemail());

            Assert.Contains("201", html);
            Assert.Contains("Jo Bloggs", html);
            Assert.Contains("07700 900123", html);
            Assert.Contains("pbx-lab", html);
            Assert.Contains("1:05", html);
        }

        [Fact]
        public void Everything_it_is_given_is_html_encoded()
        {
            var voicemail = Voicemail();
            voicemail.CallerName = "<script>alert('x')</script>";
            voicemail.MailboxName = "Sales & Support";
            voicemail.Transcript = "call me back <urgently>";

            var html = VoicemailEmailRenderer.Render(voicemail);

            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;", html);
            Assert.Contains("Sales &amp; Support", html);
            Assert.Contains("call me back &lt;urgently&gt;", html);
        }

        [Fact]
        public void A_message_with_no_transcript_carries_no_transcript_block()
        {
            var voicemail = Voicemail();
            voicemail.Transcript = null;

            var html = VoicemailEmailRenderer.Render(voicemail);

            Assert.DoesNotContain("{{Transcript", html);
            Assert.DoesNotContain("Transcribed on this server", html);
            Assert.DoesNotContain("{{Transcript", html);
        }

        /// <summary>
        /// Transcription fails open (D128), and an empty string is one of the ways it does: a
        /// heading over nothing is worse than no heading.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\n")]
        public void A_transcript_of_nothing_is_no_transcript(string transcript)
        {
            var voicemail = Voicemail();
            voicemail.Transcript = transcript;

            Assert.DoesNotContain("Transcribed on this server", VoicemailEmailRenderer.Render(voicemail));
        }

        [Fact]
        public void A_message_with_a_transcript_keeps_the_block_and_loses_the_markers()
        {
            var html = VoicemailEmailRenderer.Render(Voicemail());

            Assert.DoesNotContain("{{TranscriptBlockStart}}", html);
            Assert.DoesNotContain("{{TranscriptBlockEnd}}", html);
            Assert.Contains("Transcript", html);
            Assert.Contains("Hello, it is Jo", html);
        }

        /// <summary>Lines in a transcript stay lines rather than running together.</summary>
        [Fact]
        public void A_transcript_of_several_lines_keeps_them()
        {
            var voicemail = Voicemail();
            voicemail.Transcript = "first line\nsecond line";

            var html = VoicemailEmailRenderer.Render(voicemail);

            Assert.Contains("first line<br />", html);
            Assert.Contains("second line", html);
        }

        /// <summary>
        /// A mailbox may be set to email without the audio, and a line promising an attachment
        /// that is not there sends somebody looking for it.
        /// </summary>
        [Fact]
        public void The_attachment_line_is_only_there_when_there_is_one()
        {
            var attached = Voicemail();
            var not = Voicemail();
            not.RecordingAttached = false;

            Assert.Contains("attached to this message", VoicemailEmailRenderer.Render(attached));
            Assert.DoesNotContain("attached to this message", VoicemailEmailRenderer.Render(not));
            Assert.DoesNotContain("{{Attachment", VoicemailEmailRenderer.Render(not));
        }

        [Theory]
        [InlineData(0, "0:00")]
        [InlineData(5, "0:05")]
        [InlineData(65, "1:05")]
        [InlineData(600, "10:00")]
        [InlineData(-3, "0:00")]
        public void The_length_reads_as_minutes_and_seconds(int seconds, string expected)
        {
            Assert.Equal(expected, VoicemailEmailRenderer.Duration(seconds));
        }

        /// <summary>
        /// Who it is from, in one phrase, however much of it the trunk actually sent. A withheld
        /// number and no name is the case that must still say something.
        /// </summary>
        [Theory]
        [InlineData("Jo Bloggs", "07700900123", "Jo Bloggs (07700900123)")]
        [InlineData("", "07700900123", "07700900123")]
        [InlineData("Jo Bloggs", "", "Jo Bloggs")]
        [InlineData("07700900123", "07700900123", "07700900123")]
        [InlineData("", "", VoicemailEmailRenderer.UnknownCaller)]
        public void The_caller_is_named_by_whatever_the_trunk_sent(string name, string number, string expected)
        {
            var voicemail = Voicemail();
            voicemail.CallerId = number;
            voicemail.CallerName = name;

            Assert.Equal(expected, VoicemailEmailRenderer.Caller(voicemail));
        }

        /// <summary>
        /// The subject keeps the shape app_voicemail's own had (D126), so a mail rule written for
        /// the old message still matches the new one.
        /// </summary>
        [Fact]
        public void The_subject_says_who_it_is_from_and_which_mailbox_it_is_in()
        {
            Assert.Equal(
                "New voicemail from Jo Bloggs (07700 900123) in mailbox 201",
                VoicemailEmailRenderer.Subject(Voicemail()));
        }

        private static VoicemailEmail Voicemail() => new()
        {
            CallerId = "07700 900123",
            CallerName = "Jo Bloggs",
            DurationSeconds = 65,
            Hostname = "pbx-lab",
            Mailbox = "201",
            MailboxName = "Reception",
            ReceivedAt = new DateTimeOffset(2026, 9, 21, 9, 14, 3, TimeSpan.FromHours(1)),
            RecordingAttached = true,
            Transcript = "Hello, it is Jo",
        };
    }
}
