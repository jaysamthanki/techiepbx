using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// What the settings page knows about one key that the key itself cannot say: what it is for,
    /// in words an admin can act on, and what the code falls back to when nobody has set it.
    /// </summary>
    public class SettingDescriptor
    {
        /// <summary>
        /// The values this key may have, when they are a list rather than free text: the form shows
        /// a dropdown of them instead of a box (D75). Empty means free text, which is most keys.
        ///
        /// There is no blank entry in that dropdown, so the way back to the built-in default is the
        /// form's "Reset to default" button rather than clearing a box. Whatever is listed first
        /// should therefore be the default, so that choosing it and resetting mean the same thing.
        /// </summary>
        public IReadOnlyList<string> Choices { get; set; } = Array.Empty<string>();

        /// <summary>
        /// What happens when the setting is not stored, written the way the table should show it.
        /// Blank means there is no default and the feature simply does not work without a value.
        /// </summary>
        public string Default { get; set; } = "";

        /// <summary>One sentence: what this changes, and what it is written into.</summary>
        public string Description { get; set; } = "";

        /// <summary>Whether the value is a credential, and so never rendered into the page.</summary>
        public bool IsSecret => SettingsKeys.IsSecret(this.Key);

        public string Key { get; set; } = "";

        /// <summary>What an empty box means here, shown in the input. Usually the default.</summary>
        public string Placeholder => this.Default;

        /// <summary>What the value looks like, shown as the input's example. Blank shows none.</summary>
        public string Sample { get; set; } = "";
    }
}
