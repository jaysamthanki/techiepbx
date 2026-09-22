using Techie.Pbx.Asterisk.Config;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What the notify endpoint is willing to open (D129). The path arrives over HTTP from the
    /// mailcmd script, so this is a gate rather than a formatter: anything that is not exactly a
    /// message app_voicemail wrote, in the mailbox the request is about, is refused before a file
    /// is touched.
    /// </summary>
    public class VoicemailSpoolTests
    {
        [Fact]
        public void A_real_message_path_is_accepted()
        {
            Assert.Null(VoicemailSpool.Problem("201", "/var/spool/asterisk/voicemail/default/201/INBOX/msg0000"));
        }

        [Fact]
        public void The_path_one_message_lives_at_is_built_the_way_asterisk_writes_it()
        {
            Assert.Equal(
                "/var/spool/asterisk/voicemail/default/201/INBOX/msg0007",
                VoicemailSpool.Message("default", "201", 7));

            Assert.Equal(
                "/var/spool/asterisk/voicemail/default/201/INBOX/msg0123",
                VoicemailSpool.Message("default", "201", 123));
        }

        /// <summary>
        /// The one that matters. Every one of these is a path somebody could post, and not one of
        /// them may open a file.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("msg0000")]
        [InlineData("/etc/passwd")]
        [InlineData("/var/spool/asterisk/voicemail/default/201/INBOX/../../../../../etc/passwd")]
        [InlineData("/var/spool/asterisk/voicemail/default/201/INBOX/msg0000/../../../../etc/shadow")]
        [InlineData("/var/spool/asterisk/voicemail/../../../etc/passwd")]
        [InlineData("/var/spool/asterisk/voicemail/default/201/INBOX/msg0000.WAV")]
        [InlineData("/var/spool/asterisk/voicemail/default/201/Old/msg0000")]
        [InlineData("/var/spool/asterisk/voicemail/default/201/INBOX/msg00")]
        [InlineData("/var/spool/asterisk/voicemail/default/201/INBOX/msg00000")]
        [InlineData("/var/spool/asterisk/voicemail/default/201/INBOX/msg0000 ; rm -rf /")]
        [InlineData("//var/spool/asterisk/voicemail/default/201/INBOX/msg0000")]
        [InlineData("/var/spool/asterisk/voicemail/default/201/INBOX/msg0000\n")]
        public void Anything_that_is_not_a_message_is_refused(string path)
        {
            Assert.NotNull(VoicemailSpool.Problem("201", path));
        }

        [Fact]
        public void A_path_that_is_null_is_refused_rather_than_thrown_at()
        {
            Assert.NotNull(VoicemailSpool.Problem("201", null));
        }

        /// <summary>
        /// A perfectly well-formed path into somebody else's mailbox is still somebody else's
        /// mailbox, and this is the check no pattern can make on its own.
        /// </summary>
        [Fact]
        public void One_mailbox_may_not_ask_for_another_mailboxs_messages()
        {
            var problem = VoicemailSpool.Problem("201", "/var/spool/asterisk/voicemail/default/202/INBOX/msg0000");

            Assert.NotNull(problem);
            Assert.Contains("202", problem);
        }

        /// <summary>
        /// A site that renamed its context is still a site: the context may be anything ordinary,
        /// and nothing else.
        /// </summary>
        [Fact]
        public void The_context_may_be_a_name_and_not_a_path()
        {
            Assert.Null(VoicemailSpool.Problem("201", "/var/spool/asterisk/voicemail/other-site/201/INBOX/msg0000"));
            Assert.NotNull(VoicemailSpool.Problem("201", "/var/spool/asterisk/voicemail/a/b/201/INBOX/msg0000"));
        }

        [Fact]
        public void A_path_longer_than_any_real_one_is_refused_before_it_is_matched()
        {
            Assert.NotNull(VoicemailSpool.Problem("201", "/var/spool/asterisk/voicemail/" + new string('a', 300) + "/201/INBOX/msg0000"));
        }
    }
}
