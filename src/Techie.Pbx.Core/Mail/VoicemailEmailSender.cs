using System.Globalization;
using log4net;

namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// Sends the voicemail email this application composes itself (D129): the D129 template as the
    /// body, the transcript inside it, and the recording hung on the message as an MP3.
    ///
    /// <b>SMTP only, deliberately</b>, which is the same limitation voicemail email has had since
    /// D126 and for a harder reason here: the recording is an attachment, and the Graph sender
    /// posts a JSON message with a body and no parts. A site on Graph that wants voicemail email
    /// fills in the SMTP relay as well, and the <c>voicemail-mail</c> script — which is also SMTP
    /// only — is what delivers the message when this path cannot.
    /// </summary>
    public class VoicemailEmailSender
    {
        /// <summary>What an MP3 is, as a mail client reads it.</summary>
        public const string Mp3ContentType = "audio/mpeg";

        /// <summary>What the original recording is, when ffmpeg could not make an MP3 of it.</summary>
        public const string WavContentType = "audio/wav";

        private static readonly ILog Log = LogManager.GetLogger(typeof(VoicemailEmailSender));

        private readonly MailSettings settings;

        public VoicemailEmailSender(MailSettings settings)
        {
            this.settings = settings;
        }

        /// <summary>
        /// The recording as something to attach. The name is built from the mailbox and the time
        /// the message arrived — never from anything a caller said — so a folder of saved
        /// voicemail sorts itself and no stranger ever chooses a file name here.
        /// </summary>
        public static MailAttachment Attachment(string mailbox, DateTimeOffset receivedAt, byte[] audio, bool isMp3)
        {
            var stamp = receivedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var name = $"voicemail-{mailbox}-{stamp}{(isMp3 ? ".mp3" : ".wav")}";

            return new MailAttachment(audio, name, isMp3 ? Mp3ContentType : WavContentType);
        }

        /// <summary>
        /// Sends one voicemail to one address. The result is the whole answer, as everywhere else
        /// in this namespace: it either reached a relay that accepted it, or it says what is
        /// missing — and the caller (the notify endpoint) turns that into the non-200 that makes
        /// the script fall back to relaying app_voicemail's own message instead.
        /// </summary>
        public Task<MailResult> SendAsync(string to, VoicemailEmail voicemail, MailAttachment? attachment, CancellationToken cancellationToken)
        {
            // The address, the mailbox and whether there is a recording. Never the transcript:
            // what a caller said is the message itself, and it does not belong in a log file.
            Log.Info($"Emailing the voicemail in mailbox {voicemail.Mailbox} to {to}" +
                     $" ({(attachment == null ? "no recording" : attachment.ContentType)})");

            return new SmtpMailSender(this.settings).SendAsync(
                to,
                VoicemailEmailRenderer.Subject(voicemail),
                VoicemailEmailRenderer.Render(voicemail),
                attachment,
                cancellationToken);
        }
    }
}
