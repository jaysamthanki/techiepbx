namespace Techie.Pbx.Web.Pages.Phones
{
    /// <summary>
    /// What the background image modal and the status line show, and what the modal posts back
    /// (D145, D151). Only the file is posted; the rest is read off the disk each time.
    /// </summary>
    public class BackgroundForm
    {
        public List<string> Errors { get; set; } = new();

        /// <summary>Whether there is an image on disk right now. Not posted back.</summary>
        public bool HasImage { get; set; }

        /// <summary>
        /// The uploaded file. Its name and content type are ignored: the first bytes decide whether
        /// it is a PNG or a JPEG, and the stored name is fixed (D152).
        /// </summary>
        public IFormFile? Image { get; set; }

        /// <summary>How the current image reads, e.g. "PNG, 142 KB", or that there is none. Not posted back.</summary>
        public string Summary { get; set; } = "";
    }
}
