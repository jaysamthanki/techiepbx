namespace Techie.Pbx.Web.Pages.Moh
{
    /// <summary>
    /// What the create/edit form in the modal shows for one music on hold class, and what it posts
    /// back (D122). The text fields are nullable because model binding turns a field the user left
    /// blank into null whatever the initialiser says.
    /// </summary>
    public class MohClassForm
    {
        /// <summary>
        /// The subdirectory of the music on hold path this class plays. Offered as a slug of the
        /// name when the class is new, and left alone afterwards: it is where the files already are.
        /// </summary>
        public string? Directory { get; set; } = "";

        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// Whether this is the class that ships with the product. Not posted back — the form shows
        /// it to explain why there is no Delete button.
        /// </summary>
        public bool IsDefault { get; set; }

        public bool IsNew => this.MohClassID == 0;

        /// <summary>
        /// Whether the parking settings point at this class, which is the other reason it cannot be
        /// deleted. Not posted back.
        /// </summary>
        public bool IsParkingClass { get; set; }

        public long MohClassID { get; set; }

        public string? Name { get; set; } = "";

        /// <summary>How many tracks are in it, so the delete confirmation can say what goes with it.</summary>
        public int Tracks { get; set; }
    }
}
