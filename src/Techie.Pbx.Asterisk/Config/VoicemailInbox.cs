using System.Text.RegularExpressions;
using log4net;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// How many unread voicemails a mailbox holds, counted on disk (D169). app_voicemail keeps one
    /// <c>msgNNNN.txt</c> per message, and a message stays in <c>INBOX</c> until it is listened to
    /// and then moves to <c>Old</c> — so the <c>.txt</c> files in INBOX are exactly the new
    /// messages MWI reports. Read-only: nothing here writes, moves or opens a message.
    ///
    /// The spool rather than AMI because AMI's VoicemailUsersList is refused by our manager.conf
    /// allowlist, and widening that is a decision this does not need. The root is a parameter so
    /// the tests can point it at a temporary folder; the app always hands in
    /// <see cref="VoicemailSpool.Root"/>.
    /// </summary>
    public static partial class VoicemailInbox
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(VoicemailInbox));

        /// <summary>
        /// The unread messages in one mailbox. A mailbox that has never had a message has no
        /// folder yet, which is zero, and so is one that cannot be read — a badge is not worth
        /// failing a page over, so the reason is logged instead. A context or mailbox that is not
        /// the shape app_voicemail uses is zero without touching the disk: these become a path.
        /// </summary>
        public static int Count(string root, string context, string mailbox)
        {
            if (!ContextPattern().IsMatch(context) || !MailboxPattern().IsMatch(mailbox))
                return 0;

            var inbox = Path.Combine(root, context, mailbox, VoicemailSpool.Folder);

            try
            {
                return Directory.Exists(inbox) ? Directory.EnumerateFiles(inbox, "msg*.txt").Count() : 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not count the voicemail in {inbox}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>The unread messages across these mailboxes, all in the one context.</summary>
        public static int Unread(string root, string context, IEnumerable<string> mailboxes) =>
            mailboxes.Distinct(StringComparer.Ordinal).Sum(mailbox => Count(root, context, mailbox));

        /// <summary>The same context shape <see cref="VoicemailSpool"/> accepts.</summary>
        [GeneratedRegex(@"\A[A-Za-z0-9_-]{1,64}\z")]
        private static partial Regex ContextPattern();

        /// <summary>An extension number, which is all a mailbox here ever is.</summary>
        [GeneratedRegex(@"\A[0-9]{2,6}\z")]
        private static partial Regex MailboxPattern();
    }
}
