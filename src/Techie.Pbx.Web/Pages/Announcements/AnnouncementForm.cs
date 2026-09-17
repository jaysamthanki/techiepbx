namespace Techie.Pbx.Web.Pages.Announcements
{
    /// <summary>
    /// What the create/edit form in the modal shows, and what it posts back. The text fields are
    /// nullable because model binding turns a field the user left blank into null whatever the
    /// initialiser says.
    /// </summary>
    public class AnnouncementForm
    {
        public long AnnouncementID { get; set; }

        /// <summary>
        /// The uploaded or recorded file, or null when the admin only changed the other fields.
        /// Whatever name the browser put on it is ignored: the stored name is derived from
        /// <see cref="Name"/> (D55).
        /// </summary>
        public IFormFile? Audio { get; set; }

        /// <summary>
        /// Set when the row names an audio file that is not on disk. Worth saying out loud in the
        /// form, because the announcement looks fine in every other way and a call to it would
        /// reach a Playback of nothing.
        /// </summary>
        public bool AudioMissing { get; set; }

        /// <summary>How the current audio reads in the form, e.g. "12 s, 190 KB". Not posted back.</summary>
        public string AudioSummary { get; set; } = "";

        public string? Description { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public List<string> Errors { get; set; } = new();

        /// <summary>Whether there is audio on disk right now. Not posted back.</summary>
        public bool HasAudio { get; set; }

        public bool IsNew => this.AnnouncementID == 0;
        public string? Name { get; set; } = "";
        public string? PlayExtension { get; set; } = "";
    }
}
