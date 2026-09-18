namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// What the edit modal shows for one setting: the key being edited, the value in the box, and
    /// whatever validation rejected last time. The form posts straight back into this shape.
    ///
    /// <see cref="Value"/> is empty for a secret, always: the stored one is never rendered into
    /// the page (D68). Blank on a secret therefore means "leave it alone" rather than "clear it",
    /// which is what the Reset to default button is for.
    /// </summary>
    public class SettingForm
    {
        /// <summary>The words and the default for this key, filled in by the page.</summary>
        public SettingDescriptor Descriptor { get; set; } = new();

        public List<string> Errors { get; set; } = new();

        /// <summary>Whether anything is stored for this key at the moment.</summary>
        public bool IsSet { get; set; }

        public string? Key { get; set; } = "";

        public string? Value { get; set; } = "";
    }
}
