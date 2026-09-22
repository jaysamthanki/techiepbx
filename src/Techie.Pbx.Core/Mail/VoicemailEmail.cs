namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// What one voicemail email says, before it is poured into the D129 template. Plain values
    /// only, exactly like <see cref="AlertEmail"/>: every one of them is HTML-encoded on the way
    /// in, so nothing here may contain markup and nothing a caller says — a caller ID name is a
    /// string a stranger chose — can break the layout or smuggle a tag into somebody's inbox.
    ///
    /// Nothing here is audio. The recording travels as a <see cref="MailAttachment"/> beside the
    /// rendered body, so the renderer stays a pure function over text.
    /// </summary>
    public class VoicemailEmail
    {
        /// <summary>The caller's number, as Asterisk saw it. Blank when the caller withheld it.</summary>
        public string CallerId { get; set; } = "";

        /// <summary>The caller's name, when the trunk sent one. Usually blank.</summary>
        public string CallerName { get; set; } = "";

        /// <summary>How long the recording is, in seconds, as app_voicemail counted it.</summary>
        public int DurationSeconds { get; set; }

        /// <summary>Which box took the message. Shown in the header band and again in the footer.</summary>
        public string Hostname { get; set; } = "";

        /// <summary>The mailbox number the message is in, which is the extension's number.</summary>
        public string Mailbox { get; set; } = "";

        /// <summary>The extension's name, so the email says whose mailbox it is.</summary>
        public string MailboxName { get; set; } = "";

        /// <summary>When it arrived, in this server's local time.</summary>
        public DateTimeOffset ReceivedAt { get; set; }

        /// <summary>
        /// Whether the recording is really attached. A mailbox may be set to email without the
        /// audio, and a message promising an attachment that is not there sends somebody looking
        /// for it.
        /// </summary>
        public bool RecordingAttached { get; set; }

        /// <summary>
        /// What the caller said, when this mailbox asked for a transcript and the box produced one
        /// (D128). Null drops the whole transcript block: transcription fails open, so "no
        /// transcript" is an ordinary outcome rather than an empty heading.
        /// </summary>
        public string? Transcript { get; set; }
    }
}
