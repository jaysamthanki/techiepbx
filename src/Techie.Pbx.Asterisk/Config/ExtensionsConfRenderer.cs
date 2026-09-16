using System.Text;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders extensions.conf (the dialplan). Each extension gets an explicit entry rather
    /// than a pattern, so only numbers that exist in the database can be dialled.
    /// </summary>
    public static class ExtensionsConfRenderer
    {
        public const string InternalContext = "internal";
        public const string EchoTestNumber = "*43";

        /// <summary>FreePBX's number for "listen to my own messages", which users already know.</summary>
        public const string VoicemailMainNumber = "*97";

        public static string Render(IEnumerable<Extension> extensions)
        {
            var enabled = ConfText.EnabledInOrder(extensions);

            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append($"[{InternalContext}]\n");
            sb.Append("; Echo test\n");
            sb.Append($"exten => {EchoTestNumber},1,Answer()\n");
            sb.Append(" same => n,Playback(demo-echotest)\n");
            sb.Append(" same => n,Echo()\n");
            sb.Append(" same => n,Hangup()\n");

            // Nobody has a mailbox, so the feature code would only ever say "no such mailbox".
            if (enabled.Any(e => e.VoicemailEnabled))
            {
                sb.Append('\n');
                sb.Append("; Check your own voicemail\n");
                sb.Append($"exten => {VoicemailMainNumber},1,Answer()\n");
                sb.Append($" same => n,VoiceMailMain(${{CALLERID(num)}}@{VoicemailConfRenderer.MailboxContext},s)\n");
                sb.Append(" same => n,Hangup()\n");
            }

            foreach (var extension in enabled)
            {
                var number = ConfText.Safe(extension.Number, "number");
                var name = ConfText.Safe(extension.Name, "name");

                sb.Append('\n');
                sb.Append($"; {name}\n");
                sb.Append($"exten => {number},1,Dial(PJSIP/{number},30)\n");

                if (extension.VoicemailEnabled)
                {
                    // Busy gets the "busy" greeting, everything else (no answer, phone off,
                    // congestion) gets "unavailable" (D29). Which greeting is this renderer's
                    // decision; what "send it to the mailbox" looks like is not (D36).
                    var mailbox = new Destination(DestinationType.Voicemail, extension.Number);

                    sb.Append(" same => n,GotoIf($[\"${DIALSTATUS}\" = \"BUSY\"]?busy:unavailable)\n");
                    sb.Append(DestinationDialplan.Lines(mailbox, "busy", VoicemailGreeting.Busy));
                    sb.Append(DestinationDialplan.Lines(mailbox, "unavailable", VoicemailGreeting.Unavailable));
                }
                else
                {
                    sb.Append(DestinationDialplan.Lines(Destination.Hangup));
                }
            }

            return sb.ToString();
        }
    }
}
