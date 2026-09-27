using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Mail;
using Techie.Pbx.Web.Certificates;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// The System settings page at /Settings/System: the settings about the box itself rather than
    /// about calls, in two tabs (D115). Main is what this server is — its name, its zone, the ports
    /// it answers on. Email is how it sends mail, plus the button that proves it can.
    ///
    /// Like the SIP page, it is a display shell: rows open the general settings page's edit form in
    /// the shared modal, and that page saves them, so there is one place a setting is validated,
    /// written and logged however an admin got to it.
    /// </summary>
    public class SystemSettingsModel : PageModel
    {
        private readonly SettingsRepository settings;

        public SystemSettingsModel()
        {
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>
        /// The Email tab: the transport in force, the mail setup the admin is looking at, and just
        /// that setup's Mail.* keys. The dropdown asks for this again with the setup it picked, and
        /// so does the refresh after a save, so saving a row keeps the admin on the setup they chose
        /// rather than whichever one the settings now look most like. With no setup asked for, the
        /// tab opens on the one the settings already describe.
        /// </summary>
        public PartialViewResult OnGetEmail(string? setup)
        {
            var stored = this.settings.GetAll();
            var mail = new MailSettings(stored);
            var sender = new MailSender(mail, PbxEntra.Credential);
            var chosen = MailSetup.For(setup) ?? MailSetup.For(MailSetup.Guess(mail, sender.Transport));

            return this.Partial("_EmailTab", new EmailTab
            {
                GraphAvailable = sender.GraphAvailable,
                Sections = chosen?.Sections(stored) ?? new List<SettingSection>(),
                Setup = chosen,
                Transport = sender.Transport,
            });
        }

        /// <summary>The Main tab: what this server calls itself, and what it answers on.</summary>
        public PartialViewResult OnGetMain()
        {
            var stored = this.settings.GetAll();

            var sections = new List<SettingSection>
            {
                new()
                {
                    Help = "What this server calls itself, and the zone its open hours are written in.",
                    Rows = Rows(stored, SettingsKeys.SystemHostname, SettingsKeys.SystemTimezone),
                    Title = "Identity",
                },
                new()
                {
                    Help = "The web request log, read on the Logs page under Status. It is the way to tell whether a " +
                        "desk phone reached this server at all, so it is on by default. This one is read when the " +
                        "service starts: restart tnpbx-web after changing it (D116).",
                    Rows = Rows(stored, SettingsKeys.WebRequestLog),
                    Title = "Request log",
                },
            };

            // The ports actually in force, which is the rule Program.cs ran at startup against the
            // certificate Kestrel ended up with — a row loaded since then has not reached Kestrel
            // (D99), so asking the database again would show ports this process is not on.
            return this.Partial("_MainTab", new MainTab
            {
                Bindings = WebBindings.For(KestrelCertificate.LoadedID != 0),
                Sections = sections,
            });
        }

        private static List<SettingRow> Rows(IReadOnlyDictionary<string, string> stored, params string[] keys) =>
            keys.Select(key => SettingRow.For(SettingsCatalog.For(key), stored)).ToList();
    }
}
