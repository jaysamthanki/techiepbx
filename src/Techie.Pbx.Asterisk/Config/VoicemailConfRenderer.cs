using System.Text;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders voicemail.conf: one mailbox per extension that asked for one. Pure function: no I/O.
    /// Extensions without voicemail, and disabled extensions, get no mailbox at all.
    ///
    /// The [general] section is where voicemail to email lives (D126): the message template, and
    /// the <c>mailcmd</c> that hands the finished email to our own relay script. Nothing about the
    /// relay itself is here — no host, no credential — so this stays a pure function of the
    /// extensions, and a mail setting changing needs no apply.
    /// </summary>
    public static class VoicemailConfRenderer
    {
        /// <summary>
        /// The context every mailbox lives in. Asterisk's own default, and the dialplan names it
        /// explicitly in every VoiceMail() call, so a different one would buy nothing.
        /// </summary>
        public const string MailboxContext = "default";

        /// <summary>
        /// What <c>mailcmd</c> points at: the repo's own relay script, installed root-owned at a
        /// fixed path (D126). A constant rather than a setting because there is exactly one, and a
        /// setting here would be a command line an admin could point anywhere.
        /// </summary>
        public const string MailCommand = "/opt/tnpbx/bin/voicemail-mail";

        /// <summary>
        /// How <c>${VM_DATE}</c> is written. FreePBX's format, which reads as a sentence rather
        /// than as a timestamp: "Monday, September 21, 2026 at 09:14:03 AM".
        /// </summary>
        private const string DateFormat = "%A, %B %d, %Y at %r";

        /// <summary>
        /// The body of the email, as one config value: app_voicemail turns the escapes into real
        /// line breaks and tabs, and substitutes the variables it sets per message (D126).
        ///
        /// Only variables app_voicemail actually substitutes appear here — VM_NAME, VM_DUR,
        /// VM_MSGNUM, VM_MAILBOX, VM_CIDNUM and VM_DATE. Anything else would arrive as the literal
        /// text of the variable name, which is how these templates usually go wrong.
        /// </summary>
        private const string EmailBody =
            "Dear ${VM_NAME},\\n\\n" +
            "There is a new voicemail message in mailbox ${VM_MAILBOX}.\\n\\n" +
            "\\tFrom:     ${VM_CIDNUM}\\n" +
            "\\tReceived: ${VM_DATE}\\n" +
            "\\tLength:   ${VM_DUR}\\n" +
            "\\tMessage:  number ${VM_MSGNUM}\\n\\n" +
            "Dial *97 from your phone to listen to it.\\n\\n" +
            "-- TNPBX\\n";

        /// <summary>The subject line, FreePBX's wording: what, where and who from.</summary>
        private const string EmailSubject = "New message ${VM_MSGNUM} in mailbox ${VM_MAILBOX} from ${VM_CIDNUM}";

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
            sb.Append($"emailbody = {EmailBody}\n");
            sb.Append($"emaildateformat = {DateFormat}\n");
            sb.Append($"emailsubject = {EmailSubject}\n");
            // Two formats, wav49 first: app_voicemail attaches the FIRST format in this list, and
            // a raw .g722 is a file no mail client will play, so the narrowband GSM-in-WAV copy
            // leads (D126). g722 stays second because playback picks the best format on disk that
            // the listening channel supports, whatever order they are written in (D117).
            sb.Append("format = wav49|g722\n");
            // Delivery: a fixed, root-owned script that relays what app_voicemail composed through
            // the Mail.Smtp.* settings (D126). No serveremail or fromstring — the From header is
            // the one from those same settings, put on by the script, so there is one answer to
            // "who does this system send mail as" rather than two that can drift.
            sb.Append($"mailcmd = {MailCommand}\n");
            sb.Append($"maxmsg = {MaxMessages}\n");
            sb.Append($"maxsecs = {MaxSeconds}\n");
            sb.Append($"minsecs = {MinSeconds}\n");

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
