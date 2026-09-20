namespace Techie.Pbx.Web.Pages.Parking
{
    /// <summary>
    /// One line of the music on hold table. The order the rows arrive in is the order Asterisk
    /// plays them, because the class sorts its directory alphabetically (D119).
    /// </summary>
    public class MohFileRow
    {
        /// <summary>How the audio reads: how long it runs and how big it is, or why there is none.</summary>
        public string Audio { get; set; } = "";

        /// <summary>Whether there is something to play. False shows the summary as a warning.</summary>
        public bool AudioUsable { get; set; }

        /// <summary>What the track is stored as on disk, which is what an admin would look for.</summary>
        public string File { get; set; } = "";

        public long MohFileID { get; set; }

        public string Name { get; set; } = "";
    }
}
