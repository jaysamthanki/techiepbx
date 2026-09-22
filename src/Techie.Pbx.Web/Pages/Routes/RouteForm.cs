using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Routes
{
    /// <summary>
    /// What the create/edit form in the modal shows, and what it posts back. The text fields are
    /// nullable because model binding turns a field the user left blank into null whatever the
    /// initialiser says.
    /// </summary>
    public class RouteForm
    {
        /// <summary>
        /// What calls out over this route present as, when the extension that dialled claimed
        /// nothing of its own (D125). Blank means none, and then the trunk says who we are.
        /// </summary>
        public string? CallerID { get; set; } = "";

        public string? DialPattern { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public List<string> Errors { get; set; } = new();
        public bool IsNew => this.OutboundRouteID == 0;

        /// <summary>The classes that can be chosen, filled in by the page. Not posted back.</summary>
        public List<MohClass> MohClasses { get; set; } = new();

        /// <summary>
        /// The class a caller who went out this way hears when the far side holds them, or null for
        /// the default — the blank first option in the dropdown (D125).
        /// </summary>
        public long? MohClassID { get; set; }

        public string? Name { get; set; } = "";
        public long OutboundRouteID { get; set; }

        /// <summary>
        /// Digits put in front of the number the trunk is given (D109). Blank means none; the
        /// placeholder is the home-area-code example.
        /// </summary>
        public string? PrependDigits { get; set; } = "";

        /// <summary>Lower is tried first; the default leaves room either side.</summary>
        public int Priority { get; set; } = 100;

        /// <summary>How many dialled digits to drop before the number is sent (D109).</summary>
        public int StripDigits { get; set; }

        public long TrunkID { get; set; }

        /// <summary>
        /// The trunks that can be chosen: enabled ones only, because a route to a disabled trunk
        /// is a route to nowhere and the repository refuses it anyway. Not posted back.
        /// </summary>
        public List<Trunk> Trunks { get; set; } = new();
    }
}
