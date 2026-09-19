namespace Techie.Pbx.Web.Pages.Status
{
    /// <summary>
    /// One entry in the counts strip at the foot of the page: "9 phones", linking to the phones
    /// page. Every row is counted, enabled or not, because the strip answers "how big is this
    /// system" rather than "what is running".
    /// </summary>
    public class CountLink
    {
        public int Count { get; set; }

        /// <summary>What to call several of them, e.g. "time conditions".</summary>
        public string Plural { get; set; } = "";

        /// <summary>What to call one of them, because "1 time conditions" reads like a bug.</summary>
        public string Singular { get; set; } = "";

        /// <summary>Where the page for these lives, already resolved by the page model.</summary>
        public string Url { get; set; } = "";

        public string Text => $"{this.Count} {(this.Count == 1 ? this.Singular : this.Plural)}";
    }
}
