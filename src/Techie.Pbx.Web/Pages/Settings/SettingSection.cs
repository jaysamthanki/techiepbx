namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// A group of settings on the SIP page, with the sentence that says why they belong together.
    /// The general settings page lists every key in one table on purpose (D67); this page is the
    /// same rows, sorted into the four questions an admin actually asks about SIP (D74).
    /// </summary>
    public class SettingSection
    {
        /// <summary>The sentence under the heading. Blank shows none.</summary>
        public string Help { get; set; } = "";

        public List<SettingRow> Rows { get; set; } = new();

        public string Title { get; set; } = "";
    }
}
