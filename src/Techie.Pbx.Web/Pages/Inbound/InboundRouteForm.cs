using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Pages.Shared;

namespace Techie.Pbx.Web.Pages.Inbound
{
    /// <summary>
    /// What the create/edit form in the modal shows, and what it posts back. The text fields are
    /// nullable because model binding turns a field the user left blank into null whatever the
    /// initialiser says.
    /// </summary>
    public class InboundRouteForm
    {
        public bool CatchAll { get; set; }

        /// <summary>The destination as one string, which is what the shared picker posts (D35).</summary>
        public string? Destination { get; set; } = "";

        /// <summary>The picker itself, filled in by the page. Not posted back.</summary>
        public DestinationSelect DestinationChoices { get; set; } = new();

        public string? Description { get; set; } = "";
        public string? DID { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public List<string> Errors { get; set; } = new();
        public long InboundRouteID { get; set; }
        public bool IsNew => this.InboundRouteID == 0;

        /// <summary>The classes that can be chosen, filled in by the page. Not posted back.</summary>
        public List<MohClass> MohClasses { get; set; } = new();

        /// <summary>
        /// The music on hold class callers on this route hear while they are held, or null for the
        /// default — which is the blank first option in the dropdown (D122 amended).
        /// </summary>
        public long? MohClassID { get; set; }

        public long TrunkID { get; set; }

        /// <summary>
        /// The trunks that can be chosen: enabled ones only, because calls do not arrive on a
        /// disabled trunk. Not posted back.
        /// </summary>
        public List<Trunk> Trunks { get; set; } = new();
    }
}
