namespace Techie.Pbx.Web.Pages.Status
{
    /// <summary>
    /// One entry in the fixed allowlist <see cref="LogSources"/> keeps. <see cref="Name"/> is the
    /// only thing the browser ever sends back; everything that turns it into a path stays on the
    /// server.
    /// </summary>
    public class LogSource
    {
        /// <summary>What the dropdown shows.</summary>
        public string Label { get; set; } = "";

        /// <summary>The stable value the page's &lt;select&gt; posts, and the handler matches on.</summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// The file this source reads today, worked out at request time from the current settings
        /// — never stored, so a directory changed on the settings page takes effect on the very
        /// next request. Null means there is nothing to read yet, such as no file appender
        /// configured for the app log.
        /// </summary>
        public Func<IReadOnlyDictionary<string, string>, string?> Resolve { get; set; } = _ => null;
    }
}
