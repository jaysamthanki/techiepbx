using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Pages
{
    /// <summary>
    /// The one question the navbar asks over and over: is there config written down that Asterisk
    /// has not been given yet? (D43) Answering it is a file existence check (D26), so polling it
    /// from every open page costs nothing.
    /// </summary>
    public class ConfigStatusModel : PageModel
    {
        private readonly ConfigPendingMarker pending;

        /// <summary>Whether the database has changed since the last apply.</summary>
        public bool ConfigPending { get; private set; }

        public ConfigStatusModel()
        {
            this.pending = new ConfigPendingMarker(PbxDatabase.Current);
        }

        /// <summary>
        /// The button only - never a full page. Rendering the layout here would return the navbar,
        /// whose poll div swaps in another poll div, and htmx would refetch this in a tight loop.
        /// </summary>
        public PartialViewResult OnGet()
        {
            return this.Partial("_ApplyButton", this.pending.IsPending);
        }
    }
}
