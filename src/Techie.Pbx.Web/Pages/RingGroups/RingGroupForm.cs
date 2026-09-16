using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Pages.Shared;

namespace Techie.Pbx.Web.Pages.RingGroups
{
    /// <summary>
    /// What the create/edit form in the modal shows, and what it posts back. The text fields are
    /// nullable because model binding turns a field the user left blank into null whatever the
    /// initialiser says.
    /// </summary>
    public class RingGroupForm
    {
        public string? CallerIDPrefix { get; set; } = "";

        /// <summary>The no-answer destination as one string, which is what the picker posts (D35).</summary>
        public string? Destination { get; set; } = "";

        /// <summary>The picker itself, filled in by the page. Not posted back.</summary>
        public DestinationSelect DestinationChoices { get; set; } = new();

        public bool Enabled { get; set; } = true;
        public List<string> Errors { get; set; } = new();
        public bool IsNew => this.RingGroupID == 0;

        /// <summary>
        /// The extensions to ring, in the order they were picked. A multi-select posts one value
        /// per choice, which binds straight into this.
        /// </summary>
        public List<string> Members { get; set; } = new();

        public string? Name { get; set; } = "";
        public string? Number { get; set; } = "";
        public long RingGroupID { get; set; }
        public int RingSeconds { get; set; } = 20;
        public string? Strategy { get; set; } = RingStrategy.All.ToString();

        /// <summary>Every extension that could be a member. Not posted back.</summary>
        public List<Extension> Extensions { get; set; } = new();

        /// <summary>Whether an extension is in the group, for rendering the multi-select.</summary>
        public bool HasMember(string number) => this.Members.Contains(number, StringComparer.Ordinal);
    }
}
