using System.Text;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders voicemail.conf: one mailbox per extension that asked for one. Pure function: no I/O.
    /// Extensions without voicemail, and disabled extensions, get no mailbox at all.
    /// </summary>
    public static class VoicemailConfRenderer
    {
        /// <summary>
        /// The context every mailbox lives in. Asterisk's own default, and the dialplan names it
        /// explicitly in every VoiceMail() call, so a different one would buy nothing.
        /// </summary>
        public const string MailboxContext = "default";

        /// <summary>Messages kept per mailbox before Asterisk refuses new ones.</summary>
        private const int MaxMessages = 100;

        /// <summary>Longest message we record, in seconds. Five minutes is plenty for a PBX.</summary>
        private const int MaxSeconds = 300;

        /// <summary>Shortest message we keep, so a caller hanging up does not leave silence.</summary>
        private const int MinSeconds = 2;

        public static string Render(IEnumerable<Extension> extensions)
        {
            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[general]\n");
            // Two formats: g722 keeps wideband messages wideband for the phones that speak it
            // (D117), and wav49 stays as the narrowband copy every player and future email
            // attachment can open. Playback picks the best the caller supports.
            sb.Append("format = g722|wav49\n");
            sb.Append($"maxmsg = {MaxMessages}\n");
            sb.Append($"maxsecs = {MaxSeconds}\n");
            sb.Append($"minsecs = {MinSeconds}\n");

            // No serveremail, fromstring or email template: sending the mail is F4, and until it
            // exists Asterisk has nothing to send with, whatever address a mailbox carries.
            sb.Append('\n');
            sb.Append($"[{MailboxContext}]\n");

            foreach (var extension in ConfText.EnabledInOrder(extensions).Where(e => e.VoicemailEnabled))
            {
                var number = ConfText.Safe(extension.Number, "number");
                var pin = ConfText.Safe(extension.VoicemailPin, "voicemail PIN");
                var name = ConfText.SafeField(extension.Name, "name");
                var email = ConfText.SafeField(extension.VoicemailEmail, "voicemail email");

                // mailbox => password,name,email,pager,options
                sb.Append($"{number} => {pin},{name},{email},,{Options(extension, email)}\n");
            }

            return sb.ToString();
        }

        /// <summary>
        /// With no address there is nothing to attach to and nothing to delete after: "delete=yes"
        /// on a mailbox that emails nowhere is how people lose messages, so it is never written.
        /// </summary>
        private static string Options(Extension extension, string email)
        {
            if (email.Length == 0)
                return "attach=no|delete=no";

            return $"attach={YesNo(extension.VoicemailAttachRecording)}|delete={YesNo(extension.VoicemailDeleteAfterEmail)}";
        }

        private static string YesNo(bool value) => value ? "yes" : "no";
    }
}
