namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// Something about an uploaded background image that the admin who uploaded it has to be
    /// told: empty, too big, or not a PNG or JPEG. Written to be shown in the form, as
    /// <c>AudioUploadException</c> messages are, and never carrying a path.
    /// </summary>
    public class BackgroundUploadException : Exception
    {
        public BackgroundUploadException(string message)
            : base(message)
        {
        }
    }
}
