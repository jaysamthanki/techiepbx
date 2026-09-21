using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Web.Pages.Settings;

namespace Techie.Pbx.Web.Pages.Parking
{
    /// <summary>
    /// The Parking page: the call parking settings, and nothing else (D119, D122). It used to
    /// carry the music on hold tracks as well, because there was one class and a parked call was
    /// the only thing that played it; music on hold is several classes now and has a page of its
    /// own, which this one points at through the class it names.
    ///
    /// A display shell, like the SIP and System pages: rows open the general settings page's edit
    /// form in the shared modal, so a setting is validated, written and logged in exactly one
    /// place however an admin got to it.
    /// </summary>
    public class IndexModel : PageModel
    {
        private readonly SettingsRepository settings;

        public IndexModel()
        {
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>The parking settings, as the same grouped tables every settings page uses.</summary>
        public PartialViewResult OnGetSettings()
        {
            var stored = this.settings.GetAll();

            var sections = new List<SettingSection>
            {
                new()
                {
                    Help = "Press the feature code during a call to park it; the system speaks the slot number " +
                        "back to you. Dial that number from any phone to pick the call up. A call nobody claims " +
                        "rings back the phone that parked it when the timeout runs out.",
                    Rows = Rows(stored, SettingsKeys.ParkingEnabled, SettingsKeys.ParkingDtmfCode, SettingsKeys.ParkingSlots, SettingsKeys.ParkingTimeout),
                    Title = "Call parking",
                },
                new()
                {
                    Help = "What the parked caller hears. Silence, or the tracks in one of the music on hold " +
                        "classes — which one is the second setting, and the classes themselves live on the " +
                        "Music on hold page.",
                    Rows = Rows(stored, SettingsKeys.ParkingAudio, SettingsKeys.ParkingMusicClass),
                    Title = "While they wait",
                },
            };

            return this.Partial("/Pages/Shared/_Sections.cshtml", sections);
        }

        /// <summary>One row per key, through the shared builder so a secret could never leak.</summary>
        private static List<SettingRow> Rows(IReadOnlyDictionary<string, string> stored, params string[] keys) =>
            keys.Select(key => SettingRow.For(SettingsCatalog.For(key), stored)).ToList();
    }
}
