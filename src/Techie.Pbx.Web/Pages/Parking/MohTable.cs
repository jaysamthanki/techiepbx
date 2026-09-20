namespace Techie.Pbx.Web.Pages.Parking
{
    /// <summary>
    /// The music on hold half of the Parking page: the tracks, and whether anything is currently
    /// going to play them. The tracks are worth uploading before the setting is switched over, so
    /// the table is always shown — but a table of music nothing plays should say so (D119).
    /// </summary>
    public class MohTable
    {
        /// <summary>Whether the parking audio setting is music rather than silence right now.</summary>
        public bool MusicSelected { get; set; }

        public List<MohFileRow> Rows { get; set; } = new();
    }
}
