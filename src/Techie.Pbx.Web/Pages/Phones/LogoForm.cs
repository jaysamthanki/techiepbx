namespace Techie.Pbx.Web.Pages.Phones
{
    /// <summary>
    /// What the logo modal and its status line show, and what the modal posts back (D153, D154).
    /// The logo's twin of <see cref="BackgroundForm"/>: only the file is posted; the rest is read
    /// off the disk each time.
    /// </summary>
    public class LogoForm
    {
        public List<string> Errors { get; set; } = new();

        /// <summary>Whether there is a logo on disk right now. Not posted back.</summary>
        public bool HasImage { get; set; }

        /// <summary>
        /// The uploaded file. Its name and content type are ignored: the first bytes decide whether
        /// it is a PNG or a JPEG, and the stored name is fixed (D152, D153).
        /// </summary>
        public IFormFile? Image { get; set; }

        /// <summary>How the current logo reads, e.g. "PNG, 1 KB", or that there is none. Not posted back.</summary>
        public string Summary { get; set; } = "";
    }
}
