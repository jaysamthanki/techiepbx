using Techie.Pbx.Core.Mail;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// What a value from outside looks like by the time it reaches a message (D129). The caller ID
    /// name on an inbound call ends up in a subject line, and a subject line is a header: a line
    /// break in one is not a formatting problem, it is an injected header.
    /// </summary>
    public class MailTextTests
    {
        [Fact]
        public void Ordinary_text_is_left_alone()
        {
            Assert.Equal("Jo Bloggs", MailText.Plain("Jo Bloggs", 96));
        }

        [Fact]
        public void A_line_break_can_never_reach_a_header()
        {
            Assert.Equal("Jo Bcc: somebody@example.com", MailText.Plain("Jo\r\nBcc: somebody@example.com", 96));
            Assert.DoesNotContain("\n", MailText.Plain("a\nb\tc\0d", 96));
        }

        [Fact]
        public void Runs_of_space_collapse_and_the_ends_are_trimmed()
        {
            Assert.Equal("Jo Bloggs", MailText.Plain("   Jo     Bloggs   ", 96));
        }

        [Fact]
        public void Nothing_is_longer_than_it_is_allowed_to_be()
        {
            Assert.Equal(10, MailText.Plain(new string('x', 500), 10).Length);
            Assert.Equal("", MailText.Plain(null, 10));
        }

        /// <summary>
        /// A transcript may have lines in it and they are worth keeping; everything else that is
        /// not text still is not text.
        /// </summary>
        [Fact]
        public void A_block_keeps_its_lines_and_loses_everything_else()
        {
            Assert.Equal("first line\nsecond line", MailText.Block("  first line\r\nsecond line\r\n", 100));
            Assert.Equal("a b", MailText.Block("a\tb", 100));
        }

        [Fact]
        public void A_block_is_capped_too()
        {
            Assert.Equal(20, MailText.Block(new string('x', 5000), 20).Length);
        }
    }
}
