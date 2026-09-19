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
        public string? DialPattern { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public List<string> Errors { get; set; } = new();
        public bool IsNew => this.OutboundRouteID == 0;
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
