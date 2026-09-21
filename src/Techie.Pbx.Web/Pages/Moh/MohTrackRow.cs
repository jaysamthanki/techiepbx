namespace Techie.Pbx.Web.Pages.Moh
{
    /// <summary>
    /// One line of the music on hold track table. The rows arrive grouped by class and then in the
    /// order Asterisk plays them, because a class sorts its directory alphabetically (D119, D122).
    /// </summary>
    public class MohTrackRow
    {
        /// <summary>How the audio reads: how long it runs and how big it is, or why there is none.</summary>
        public string Audio { get; set; } = "";

        /// <summary>Whether there is something to play. False shows the summary as a warning.</summary>
        public bool AudioUsable { get; set; }

        /// <summary>What class the track is in, which is what decides where it is played from.</summary>
        public string ClassName { get; set; } = "";

        /// <summary>What the track is stored as on disk, which is what an admin would look for.</summary>
        public string File { get; set; } = "";

        public long MohFileID { get; set; }

        public string Name { get; set; } = "";
    }
}
