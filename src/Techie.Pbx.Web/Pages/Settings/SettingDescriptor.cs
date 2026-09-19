using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

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

        /// <summary>
        /// What reads this key, which is what decides whether saving it is a config change at all
        /// (D103). A property of the key itself, like <see cref="IsSecret"/>, so the catalog cannot
        /// disagree with what the repository does.
        /// </summary>
        public SettingScope Scope => SettingsKeys.ScopeOf(this.Key);

        /// <summary>Where a change to this setting gets to, for the form to say before it is saved.</summary>
        public string ScopeNote => this.Scope switch
        {
            SettingScope.Asterisk =>
                "This setting is written into the generated config, so the change reaches Asterisk at the next apply.",
            SettingScope.Phones =>
                "This setting is written into the phone configs, which are generated per request, " +
                "so each phone picks the change up at its next poll. There is nothing to apply.",
            _ =>
                "This setting is read by this application itself, so the change takes effect straight away. " +
                "There is nothing to apply.",
        };
    }
}
