namespace Techie.Pbx.Web.Pages.Moh
{
    /// <summary>
    /// What the create/edit form in the modal shows for one music on hold track, and what it posts
    /// back (D119, D122). The text field is nullable because model binding turns a field the user
    /// left blank into null whatever the initialiser says.
    /// </summary>
    public class MohTrackForm
    {
        /// <summary>
        /// The uploaded file, or null when the admin only renamed the track. Whatever name the
        /// browser put on it is ignored: the stored name is derived from the track's ID and its
        /// name (D119).
        /// </summary>
        public IFormFile? Audio { get; set; }

        /// <summary>
        /// Set when the row names a file that is not on disk. Worth saying out loud, because the
        /// track looks fine in the table and Asterisk would simply never play it.
        /// </summary>
        public bool AudioMissing { get; set; }

        /// <summary>How the current audio reads in the form, e.g. "2.5 min, 2,400 KB". Not posted back.</summary>
        public string AudioSummary { get; set; } = "";

        /// <summary>
        /// The classes an admin may put a new track in, and what each is called. Not posted back;
        /// the chosen one is <see cref="MohClassID"/>.
        /// </summary>
        public List<MohClassRow> Classes { get; set; } = new();

        /// <summary>What the track's class is called, for the edit form, which does not move it.</summary>
        public string ClassName { get; set; } = "";

        public List<string> Errors { get; set; } = new();

        /// <summary>Whether there is audio on disk right now. Not posted back.</summary>
        public bool HasAudio { get; set; }

        public bool IsNew => this.MohFileID == 0;

        /// <summary>Which class the track belongs to, which is the directory it is played from.</summary>
        public long MohClassID { get; set; }

        public long MohFileID { get; set; }

        public string? Name { get; set; } = "";
    }
}
