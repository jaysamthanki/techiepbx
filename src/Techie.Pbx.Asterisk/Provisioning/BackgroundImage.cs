namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// The site's Polycom background image (D145) or logo (D153) as it is on disk right now. Read
    /// off the file every time rather than out of the database, so it cannot claim an image that
    /// is not there.
    /// </summary>
    public class BackgroundImage
    {
        /// <summary>The file's size, for the Phones page to show.</summary>
        public long Bytes { get; set; }

        public BackgroundImageFormat Format { get; set; }

        /// <summary>The full path of the file, inside the data folder. Never shown to anyone.</summary>
        public string Path { get; set; } = "";
    }
}
