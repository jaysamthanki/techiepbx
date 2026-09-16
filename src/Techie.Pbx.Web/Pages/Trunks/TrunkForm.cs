namespace Techie.Pbx.Web.Pages.Trunks
{
    /// <summary>
    /// What the create/edit form in the modal shows, and what it posts back. Like the extension
    /// form, the text fields are nullable: model binding turns a field the user left blank into
    /// null whatever the initialiser says.
    ///
    /// <see cref="Password"/> is never rendered into the form. Blank on an edit means "leave the
    /// stored one alone", which is how the password stays out of the browser (D41).
    /// </summary>
    public class TrunkForm
    {
        public string? AuthUsername { get; set; } = "";
        public string? CallerIDName { get; set; } = "";
        public string? CallerIDNumber { get; set; } = "";

        /// <summary>The codecs ticked, in the order the form offers them.</summary>
        public List<string> Codecs { get; set; } = new();

        public bool Enabled { get; set; } = true;
        public List<string> Errors { get; set; } = new();
        public bool IsNew => this.TrunkID == 0;
        public string? MatchAddresses { get; set; } = "";
        public string? Name { get; set; } = "";

        /// <summary>Blank leaves the stored password alone; only a new trunk has to have one.</summary>
        public string? Password { get; set; } = "";

        public bool Register { get; set; } = true;
        public string? ServerHost { get; set; } = "";

        /// <summary>The SIP port a new trunk starts on.</summary>
        public int ServerPort { get; set; } = 5060;

        public long TrunkID { get; set; }
        public string? Username { get; set; } = "";

        /// <summary>Whether a codec is ticked, for rendering the checkboxes.</summary>
        public bool HasCodec(string codec) => this.Codecs.Contains(codec, StringComparer.Ordinal);
    }
}
