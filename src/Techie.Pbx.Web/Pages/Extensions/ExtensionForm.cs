namespace Techie.Pbx.Web.Pages.Extensions
{
    /// <summary>
    /// What the create/edit form in the modal shows: the extension's fields, its voicemail
    /// settings, and whatever the repository rejected last time. A new extension has no
    /// ExtensionID yet. The form posts straight back into this shape.
    ///
    /// The text fields are nullable even though they start empty: model binding turns a field
    /// the user left blank into null, initialiser or no initialiser, and an empty PIN or email
    /// is an ordinary thing to post.
    /// </summary>
    public class ExtensionForm
    {
        public bool Enabled { get; set; } = true;
        public List<string> Errors { get; set; } = new();
        public long ExtensionID { get; set; }
        public bool IsNew => this.ExtensionID == 0;
        public string? Name { get; set; } = "";
        public string? Number { get; set; } = "";

        /// <summary>
        /// The SIP password, shown in the clear in the form for new and existing extensions
        /// alike (D112). Blank on an existing extension keeps the current one; Regenerate
        /// makes a new one.
        /// </summary>
        public string? Secret { get; set; } = "";

        public bool VoicemailAttachRecording { get; set; } = true;
        public bool VoicemailDeleteAfterEmail { get; set; }
        public string? VoicemailEmail { get; set; } = "";
        public bool VoicemailEnabled { get; set; }
        public string? VoicemailPin { get; set; } = "";
    }
}
