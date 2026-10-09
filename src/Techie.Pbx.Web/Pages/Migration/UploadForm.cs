namespace Techie.Pbx.Web.Pages.Migration
{
    /// <summary>The upload form in the modal: nothing to fill in but the file, and why it was refused.</summary>
    public class UploadForm
    {
        public List<string> Errors { get; set; } = new();
    }
}
