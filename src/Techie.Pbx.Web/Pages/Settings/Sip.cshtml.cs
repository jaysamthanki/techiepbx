using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// The SIP settings page: the Sip.* keys, grouped into transport, NAT, media and codecs (D74).
    ///
    /// It is a display shell and nothing more. Rows open the general settings page's edit form in
    /// the shared modal, and that page saves them, so there is one place a setting is validated,
    /// written and logged however an admin got to it. What comes back is the same 204 with
    /// "settingsChanged", which this page's table listens for like the other one does.
    /// </summary>
    public class SipModel : PageModel
    {
        private readonly SettingsRepository settings;

        public SipModel()
        {
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>The four sections, each a small table of rows.</summary>
        public PartialViewResult OnGetTable()
        {
            var stored = this.settings.GetAll();

            var sections = new List<SettingSection>
            {
                new()
                {
                    Help = "Which addresses and ports Asterisk listens for SIP on. UDP is always generated; " +
                        "TCP only when a TCP port is set, and TLS not at all yet.",
                    Rows = Rows(stored, SettingsKeys.SipBindAddress, SettingsKeys.SipPort, SettingsKeys.SipTcpPort, SettingsKeys.SipTlsPort),
                    Title = "Transport",
                },
                new()
                {
                    Help = "For a server behind 1:1 NAT: the public address callers see, and the private " +
                        "networks that are on this side of it. Leave both blank on a server with a public address of its own.",
                    Rows = Rows(stored, SettingsKeys.SipExternalAddress, SettingsKeys.SipLocalNets),
                    Title = "NAT",
                },
                new()
                {
                    Help = "Written into rtp.conf. A STUN server also turns ICE on, as does an external address. " +
                        "rtp.conf is only read when Asterisk starts, so a change here needs a restart, not a reload.",
                    Rows = Rows(stored, SettingsKeys.SipStunServer),
                    Title = "Media",
                },
                new()
                {
                    Help = "What extensions are offered, in preference order. Trunks keep their own codec list, " +
                        "chosen from the same three.",
                    Rows = Rows(stored, SettingsKeys.SipCodecs),
                    Title = "Codecs",
                },
            };

            return this.Partial("_Sections", sections);
        }

        private static List<SettingRow> Rows(IReadOnlyDictionary<string, string> stored, params string[] keys) =>
            keys.Select(key => SettingRow.For(SettingsCatalog.For(key), stored)).ToList();
    }
}
