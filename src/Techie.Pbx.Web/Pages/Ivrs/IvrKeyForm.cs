using Techie.Pbx.Web.Pages.Shared;

namespace Techie.Pbx.Web.Pages.Ivrs
{
    /// <summary>
    /// One row of the digit map editor: a key, and where it sends a call. The form carries a row
    /// for every key a caller could press, in keypad order, so there is nothing to add or remove
    /// and no JavaScript to do it with. The rows left on "Not used" are dropped when the menu is
    /// saved, because a digit with no entry is simply absent rather than a setting (D59).
    /// </summary>
    public class IvrKeyForm
    {
        /// <summary>The picker for this key, filled in by the page. Not posted back.</summary>
        public DestinationSelect Choices { get; set; } = new();

        /// <summary>Where this key sends a call, as the picker posts it. Empty means unused.</summary>
        public string? Destination { get; set; } = "";

        /// <summary>The key itself: 0-9, * or #. Posted hidden, so a row keeps its key.</summary>
        public string? Digit { get; set; } = "";
    }
}
