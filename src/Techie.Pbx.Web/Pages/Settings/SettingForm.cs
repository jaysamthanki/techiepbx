namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// What the edit modal shows for one setting: the key being edited, the value in the box, and
    /// whatever validation rejected last time. The form posts straight back into this shape.
    ///
    /// The box carries the stored value, secret or not (D112): the table is where the dots are.
    /// Blank on save means "back to the default", the same as for every other setting.
    /// </summary>
    public class SettingForm
    {
        /// <summary>The words and the default for this key, filled in by the page.</summary>
        public SettingDescriptor Descriptor { get; set; } = new();

        public List<string> Errors { get; set; } = new();

        /// <summary>Whether anything is stored for this key at the moment.</summary>
        public bool IsSet { get; set; }

        public string? Key { get; set; } = "";

        /// <summary>
        /// What the mail setup the admin came from normally has here (<see cref="MailSetup"/>), or
        /// blank. Only ever a starting value for the box: nothing is stored until the form is saved.
        /// </summary>
        public string Suggested { get; set; } = "";

        /// <summary>The name of the setup <see cref="Suggested"/> came from, for the form to say so.</summary>
        public string SuggestedBy { get; set; } = "";

        public string? Value { get; set; } = "";
    }
}
