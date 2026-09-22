namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// What the <c>voicemail-mail</c> script says about the message it has just been handed (D129).
    /// Every field comes out of the email app_voicemail composed, which means every field comes
    /// from somewhere outside this application: the mailbox and the path are checked against what
    /// app_voicemail can actually have written, and the caller's name and number are treated as
    /// text a stranger chose.
    /// </summary>
    public class VoicemailNotifyRequest
    {
        /// <summary>The caller's number, as Asterisk saw it.</summary>
        public string CallerId { get; set; } = "";

        /// <summary>The caller's name, when the trunk sent one.</summary>
        public string CallerName { get; set; } = "";

        /// <summary>How long the recording is, in seconds.</summary>
        public int DurationSeconds { get; set; }

        /// <summary>The mailbox number, which is an extension's number.</summary>
        public string Mailbox { get; set; } = "";

        /// <summary>
        /// The message on disk, without its extension — <c>/var/spool/asterisk/voicemail/default/
        /// 201/INBOX/msg0000</c>. Checked by <c>VoicemailSpool.Problem</c> before anything opens it.
        /// </summary>
        public string MessagePath { get; set; } = "";

        /// <summary>When the message arrived, in seconds since the epoch.</summary>
        public long ReceivedEpoch { get; set; }
    }
}
