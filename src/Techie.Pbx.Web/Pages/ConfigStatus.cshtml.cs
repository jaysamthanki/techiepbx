using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Pages
{
    /// <summary>
    /// The two questions the navbar asks over and over: is there config written down that Asterisk
    /// has not been given yet (D43), and is Asterisk still running config an apply has already
    /// replaced on disk (D104)? Answering both is two file existence checks (D26), so polling them
    /// from every open page costs nothing.
    /// </summary>
    public class ConfigStatusModel : PageModel
    {
        private readonly ConfigPendingMarker pending;
        private readonly AsteriskRestartMarker restart;

        /// <summary>What the navbar shows: the apply button, the restart banner, or neither.</summary>
        public ApplyStatus Status { get; private set; } = new();

        public ConfigStatusModel()
        {
            this.pending = new ConfigPendingMarker(PbxDatabase.Current);
            this.restart = new AsteriskRestartMarker(PbxDatabase.Current);
        }

        /// <summary>
        /// The button only - never a full page. Rendering the layout here would return the navbar,
        /// whose poll div swaps in another poll div, and htmx would refetch this in a tight loop.
        /// </summary>
        public PartialViewResult OnGet()
        {
            this.Status = new ApplyStatus
            {
                ConfigPending = this.pending.IsPending,
                RestartRequired = this.restart.IsPending,
            };

            return this.Partial("_ApplyButton", this.Status);
        }
    }
}
