namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// What one alert email says, before it is poured into the D114 template. Plain values only:
    /// every one of them is HTML-encoded on the way in, so nothing here may contain markup and
    /// nothing a caller passes can break the layout or smuggle a tag into somebody's inbox.
    /// </summary>
    public class AlertEmail
    {
        /// <summary>The bar beside the title, and the button. Something went wrong.</summary>
        public const string AccentDanger = "#dc3545";

        /// <summary>The bar beside the title, and the button. Nothing is wrong.</summary>
        public const string AccentInfo = "#0d6efd";

        /// <summary>The bar beside the title, and the button. Worth a look.</summary>
        public const string AccentWarning = "#fd7e14";

        /// <summary>The severity colour, one of the Accent constants. Defaults to information.</summary>
        public string AccentColor { get; set; } = AccentInfo;

        /// <summary>The optional call-to-action's text. Both it and the URL are needed to show one.</summary>
        public string ButtonText { get; set; } = "";

        /// <summary>The optional call-to-action's link.</summary>
        public string ButtonUrl { get; set; } = "";

        /// <summary>
        /// The paragraphs of the message, one entry per line. Encoded and joined with line breaks,
        /// so a caller writes sentences rather than markup.
        /// </summary>
        public List<string> Body { get; set; } = new();

        /// <summary>
        /// The optional key/value table under the body: what this alert is about, in detail.
        /// Empty drops the whole block rather than leaving an empty table behind.
        /// </summary>
        public List<AlertEmailDetail> Details { get; set; } = new();

        /// <summary>Which box sent it. Shown in the header band and again in the footer.</summary>
        public string Hostname { get; set; } = "";

        public string Subject { get; set; } = "";

        /// <summary>When it happened, already formatted for a reader rather than for a machine.</summary>
        public string Timestamp { get; set; } = "";
    }

    /// <summary>One row of an alert's detail table.</summary>
    public class AlertEmailDetail
    {
        public string Label { get; }

        public string Value { get; }

        public AlertEmailDetail(string label, string value)
        {
            this.Label = label;
            this.Value = value;
        }
    }
}
