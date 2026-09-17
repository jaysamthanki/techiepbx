namespace Techie.Pbx.Web.Pages.Announcements
{
    /// <summary>One line of the announcements table.</summary>
    public class AnnouncementRow
    {
        public long AnnouncementID { get; set; }

        /// <summary>The size and length of the stored file, or "No audio" when there is none.</summary>
        public string Audio { get; set; } = "";

        /// <summary>
        /// Whether the audio is actually on disk. False both for "never uploaded" and for "the row
        /// names a file that is not there", which is worth showing differently from a normal row.
        /// </summary>
        public bool AudioUsable { get; set; }

        public string Description { get; set; } = "";
        public bool Enabled { get; set; }
        public string Name { get; set; } = "";

        /// <summary>The number to dial to hear it, or an em dash when there is none.</summary>
        public string PlayExtension { get; set; } = "";
    }
}
