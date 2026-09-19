namespace Techie.Pbx.Web.Pages.Status
{
    /// <summary>
    /// One card in the row of health tiles: a heading, one line of text, and a colour. The same
    /// idea as <see cref="Shared.StatusBadge"/> — what to say and what class to say it in, worked
    /// out here so the view is a loop rather than six blocks of nearly identical markup.
    /// </summary>
    public class StatusTile
    {
        /// <summary>The coloured edge of the card, which is the whole of the colour coding.</summary>
        public string BorderClass => this.Level switch
        {
            TileLevel.Bad => "border-danger",
            TileLevel.Warn => "border-warning",
            _ => "border-success",
        };

        public TileLevel Level { get; set; }

        /// <summary>The answer, in as few words as it can be said in: "2 of 2 registered".</summary>
        public string Text { get; set; } = "";

        /// <summary>What the tile is about: "Trunks", "Certificate".</summary>
        public string Title { get; set; } = "";
    }
}
