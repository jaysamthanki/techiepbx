using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Contracts;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Web.Services;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// The firewall page: what this system expects to be open, what the Helper last applied, and
    /// one button that makes the second match the first (D142).
    ///
    /// It is a status page rather than a list page, so there is no modal and no form — nothing
    /// here is edited, because the ruleset is derived from the settings that decide what is
    /// listening. What it does have in common with the others is the shape: a shell the page
    /// loads, a handler that renders the panel, and an action that answers with the new panel.
    ///
    /// Every failure is shown. A helper that is not running, a helper that refused the ruleset
    /// and nft's own words about why all end up in <see cref="FirewallView.Error"/> and on the
    /// page: an admin who pressed Apply has to be told what happened to the firewall.
    /// </summary>
    public class FirewallModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(FirewallModel));

        private readonly HelperClient helper;
        private readonly SettingsRepository settings;

        public FirewallModel()
        {
            this.helper = new HelperClient();
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>
        /// Applies the expected ruleset. The rules are built here from the settings and not taken
        /// from the browser: there is no request shape a client could send that would open a port
        /// this system is not configured to listen on.
        /// </summary>
        public async Task<IActionResult> OnPostApply()
        {
            var expected = FirewallRulesBuilder.Build(this.settings.GetAll());

            Log.Info($"Firewall apply requested by {this.User.Identity?.Name}: {expected.Count} rule(s)");

            try
            {
                await this.helper.Apply(expected);
            }
            catch (HelperException ex)
            {
                // Nothing is hidden and nothing is retried. The panel comes back with the
                // helper's sentence on it and the tables still saying what is really applied.
                Log.Error($"Firewall apply failed: {ex.Message}");
                return this.Partial("_FirewallPanel", await this.Load(ex.Message));
            }

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["pbxToast"] = new { message = $"Firewall applied: {expected.Count} rule(s), plus the safety rules." },
            });

            return this.Partial("_FirewallPanel", await this.Load(""));
        }

        /// <summary>The whole page below the heading, which is also what an apply answers with.</summary>
        public async Task<PartialViewResult> OnGetPanel() =>
            this.Partial("_FirewallPanel", await this.Load(""));

        /// <summary>
        /// Asks the Helper what it has, works out what it should have, and compares them. The
        /// comparison is made here rather than in the Helper on purpose: the expected ruleset is
        /// this application's business, and a root process is not the place to put a rule about
        /// which settings mean which ports (D142).
        /// </summary>
        private async Task<FirewallView> Load(string error)
        {
            var view = new FirewallView
            {
                Error = error,
                Expected = FirewallRulesBuilder.Build(this.settings.GetAll()),
            };

            try
            {
                view.Version = await this.helper.Ping();
                view.HelperReachable = true;

                var status = await this.helper.Status();

                view.Applied = status.AppliedRules;
                view.AppliedAtUtc = status.AppliedAtUtc;
                view.InSync = status.AppliedAtUtc != null && FirewallRulesBuilder.Match(view.Expected, view.Applied);
            }
            catch (HelperException ex)
            {
                // An unreachable helper is a state of the page, not an error page: the expected
                // rules are still worth showing, and the badge says why nothing can be applied.
                Log.Warn($"The helper could not be reached: {ex.Message}");

                if (view.Error.Length == 0)
                    view.Error = ex.Message;
            }

            return view;
        }
    }
}
