using Techie.Pbx.Asterisk.Config;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The unread count behind the /phone Voicemail badge (D169): the <c>.txt</c> files in a
    /// mailbox's INBOX, against a fake spool in a temporary folder.
    /// </summary>
    public class VoicemailInboxTests : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("tnpbx-vm-inbox-").FullName;

        public void Dispose() => Directory.Delete(this.root, recursive: true);

        /// <summary>Writes one message the way app_voicemail does: a .txt and its recording.</summary>
        private void Message(string mailbox, string folder, int number)
        {
            var directory = Path.Combine(this.root, "default", mailbox, folder);
            Directory.CreateDirectory(directory);

            File.WriteAllText(Path.Combine(directory, $"msg{number:0000}.txt"), "[message]\n");
            File.WriteAllText(Path.Combine(directory, $"msg{number:0000}.wav"), "");
        }

        [Fact]
        public void Every_message_in_the_inbox_is_counted_once()
        {
            this.Message("201", "INBOX", 0);
            this.Message("201", "INBOX", 1);
            this.Message("201", "INBOX", 2);

            Assert.Equal(3, VoicemailInbox.Count(this.root, "default", "201"));
        }

        [Fact]
        public void Old_messages_are_not_unread()
        {
            this.Message("201", "INBOX", 0);
            this.Message("201", "Old", 0);
            this.Message("201", "Old", 1);

            Assert.Equal(1, VoicemailInbox.Count(this.root, "default", "201"));
        }

        [Fact]
        public void A_mailbox_with_no_folder_yet_has_nothing_unread()
        {
            Assert.Equal(0, VoicemailInbox.Count(this.root, "default", "201"));
        }

        [Fact]
        public void Files_that_are_not_message_metadata_are_not_counted()
        {
            var inbox = Path.Combine(this.root, "default", "201", "INBOX");
            Directory.CreateDirectory(inbox);
            File.WriteAllText(Path.Combine(inbox, "msg0000.wav"), "");
            File.WriteAllText(Path.Combine(inbox, "notes.txt"), "");

            Assert.Equal(0, VoicemailInbox.Count(this.root, "default", "201"));
        }

        [Fact]
        public void Unread_sums_the_mailboxes_and_counts_each_once()
        {
            this.Message("201", "INBOX", 0);
            this.Message("201", "INBOX", 1);
            this.Message("202", "INBOX", 0);
            this.Message("203", "INBOX", 0);

            Assert.Equal(3, VoicemailInbox.Unread(this.root, "default", new[] { "201", "202", "201" }));
        }

        /// <summary>Both become part of a path, so anything that is not their shape reads nothing.</summary>
        [Theory]
        [InlineData("default", "../201")]
        [InlineData("default", "201/../202")]
        [InlineData("default", "")]
        [InlineData("..", "201")]
        [InlineData("default/..", "201")]
        public void A_context_or_mailbox_that_is_not_one_reads_nothing(string context, string mailbox)
        {
            this.Message("201", "INBOX", 0);
            this.Message("202", "INBOX", 0);

            Assert.Equal(0, VoicemailInbox.Count(this.root, context, mailbox));
        }
    }
}
