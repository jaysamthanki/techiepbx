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
