namespace Techie.Pbx.Asterisk.Audio
{
    /// <summary>
    /// Something about an uploaded or recorded file that the admin who uploaded it has to be told:
    /// too big, not a format we accept, ffmpeg missing, or ffmpeg refusing the file.
    ///
    /// The message is written to be shown in the form, the same way
    /// <see cref="Techie.Pbx.Core.ValidationFailedException"/> messages are. It never carries a
    /// path or an ffmpeg command line: what went wrong goes to the browser, where it went wrong
    /// goes to the log.
    /// </summary>
    public class AudioUploadException : Exception
    {
        public AudioUploadException(string message)
            : base(message)
        {
        }

        public AudioUploadException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
