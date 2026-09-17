namespace Techie.Pbx.Web.Pages.TimeConditions
{
    /// <summary>
    /// The one setting this page edits: the IANA zone this server's clock is recorded as being in
    /// (D65). It lives here rather than on a settings page because there is no settings page, and
    /// because this is the only screen where the value means anything to the reader.
    /// </summary>
    public class TimezoneForm
    {
        public List<string> Errors { get; set; } = new();

        /// <summary>The zone name, e.g. "Europe/London". Blank means the built-in default.</summary>
        public string? Timezone { get; set; } = "";
    }
}
