using Techie.Pbx.Web.Certificates;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// What the System page's Main tab shows: the settings that say what this server calls itself,
    /// and the ports it is answering on (D115).
    ///
    /// The ports are shown and not edited, because they are not a setting: <see cref="WebBindings"/>
    /// decides them from whether there is a usable certificate, and 80 has to stay open for ACME
    /// renewals while 8080 is the way back in (D99). An editable "listening port" box would be a
    /// lie — nothing reads one. Changing them is a code change with a decision behind it.
    /// </summary>
    public class MainTab
    {
        public List<WebBinding> Bindings { get; set; } = new();

        public List<SettingSection> Sections { get; set; } = new();
    }
}
