namespace Techie.Pbx.Web.Pages.Extensions
{
    /// <summary>
    /// What the create/edit form in the modal shows: the extension's fields plus whatever the
    /// repository rejected last time. A new extension has no ExtensionID yet.
    /// </summary>
    public class ExtensionForm
    {
        public bool Enabled { get; set; } = true;
        public List<string> Errors { get; set; } = new();
        public long ExtensionID { get; set; }
        public bool IsNew => this.ExtensionID == 0;
        public string Name { get; set; } = "";
        public string Number { get; set; } = "";

        /// <summary>
        /// Only filled in for a new extension, where the generated password is shown once so it
        /// can be typed into the phone. Editing never carries the secret to the browser; the
        /// "show password" action does that on request.
        /// </summary>
        public string Secret { get; set; } = "";
    }
}
