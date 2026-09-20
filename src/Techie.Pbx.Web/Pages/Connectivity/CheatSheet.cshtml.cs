using System.Globalization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Pages.Connectivity
{
    /// <summary>
    /// The cheat sheet at /Connectivity/CheatSheet: who is on which extension and what a handset
    /// can dial, on one page, meant to be printed and pinned up next to the phones (D120).
    ///
    /// The one page in the application written for somebody who will never log in. It is read-only
    /// and has no settings of its own: everything on it is either a row that already exists or a
    /// code <see cref="FeatureCodes"/> derives from what the renderers generate, so the sheet
    /// cannot promise a code the dialplan has not got.
    ///
    /// Not a shell htmx fills, unlike every other page here. There is nothing to refresh, nothing
    /// to poll and nothing to submit — a printed sheet is a snapshot by definition, so the whole
    /// of it is rendered by <see cref="OnGet"/> in one go and the browser's own print dialog does
    /// the rest.
    /// </summary>
    public class CheatSheetModel : PageModel
    {
        private readonly ExtensionRepository extensions;
        private readonly SettingsRepository settings;

        /// <summary>The dialable codes this system currently generates, in printing order.</summary>
        public List<FeatureCode> Codes { get; private set; } = new();

        /// <summary>Every enabled extension, by number. Names and numbers only.</summary>
        public List<CheatSheetExtension> Numbers { get; private set; } = new();

        /// <summary>
        /// The day the sheet was printed, so the one on the wall can be told from the one in the
        /// drawer. The server's clock is UTC by design (D74), which for a date is close enough.
        /// </summary>
        public string PrintedOn { get; private set; } = "";

        /// <summary>
        /// Which box this is: the hostname an admin has set if there is one, and the machine's own
        /// name otherwise — the same two answers, in the same order, that the footer gives.
        /// </summary>
        public string SiteName { get; private set; } = "";

        public CheatSheetModel()
        {
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
            var stored = this.settings.GetAll();

            // The dialplan's own order (D12), without the renderer's re-validation: nothing here
            // is written to a conf file, and one bad row must not be able to blank the sheet.
            var enabled = this.extensions.GetAll()
                .Where(e => e.Enabled)
                .OrderBy(e => e.Number.Length)
                .ThenBy(e => e.Number, StringComparer.Ordinal)
                .ToList();

            var hostname = (stored.GetValueOrDefault(SettingsKeys.SystemHostname) ?? "").Trim();

            this.Codes = FeatureCodes.All(AsteriskSettings.Parking(stored), enabled.Any(e => e.VoicemailEnabled));
            this.Numbers = enabled
                .Select(e => new CheatSheetExtension { Name = e.Name, Number = e.Number })
                .ToList();
            this.PrintedOn = DateTime.Now.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
            this.SiteName = hostname.Length > 0 ? hostname : Environment.MachineName;
        }
    }
}
