namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// Something about an uploaded background image or logo that the admin who uploaded it has to
    /// be told: empty, too big, not a PNG or JPEG, or not something that could be decoded and
    /// resized (D154). Written to be shown in the form, as <c>AudioUploadException</c> messages
    /// are, and never carrying a path.
    /// </summary>
    public class BackgroundUploadException : Exception
    {
        public BackgroundUploadException(string message)
            : base(message)
        {
        }
    }
}
