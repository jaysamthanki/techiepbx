namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// A file to hang on a message: the bytes, what to call it, and what it is. Bytes rather than a
    /// path or a stream on purpose — the only thing this system attaches is a voicemail recording
    /// it has already converted in a temporary file, and a sender that took a path would be a
    /// sender that could be handed one (D129).
    ///
    /// The name is ours, not a caller's: it is built from the mailbox and the time by
    /// <see cref="VoicemailEmailSender.Attachment"/>, so nothing a stranger said ever becomes a
    /// file name in somebody's downloads folder.
    /// </summary>
    public class MailAttachment
    {
        public byte[] Bytes { get; }

        /// <summary>The MIME type, e.g. <c>audio/mpeg</c>.</summary>
        public string ContentType { get; }

        public string FileName { get; }

        public MailAttachment(byte[] bytes, string fileName, string contentType)
        {
            this.Bytes = bytes;
            this.ContentType = contentType;
            this.FileName = fileName;
        }
    }
}
