using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Pages.Shared;

namespace Techie.Pbx.Web.Pages.Trunks
{
    /// <summary>
    /// The trunks page, built the same way as the extensions page: a shell that htmx fills from
    /// the handlers here, changes that answer 204 with HX-Trigger events, and a five second poll
    /// for the registration badges (D21).
    /// </summary>
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly SettingsRepository settings;
        private readonly TrunkRepository trunks;

        public IndexModel()
        {
            this.settings = new SettingsRepository(PbxDatabase.Current);
            this.trunks = new TrunkRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>The create or edit form, which the page shows in a sweetalert2 modal.</summary>
        public IActionResult OnGetForm(long? trunkID)
        {
            if (trunkID is null or 0)
                return this.Partial("_Form", new TrunkForm { Codecs = new List<string> { "ulaw", "alaw" } });

            var trunk = this.trunks.GetByID(trunkID.Value);
            if (trunk == null)
                return this.NotFound();

            // No password: it is never rendered into the form, and blank means "leave it alone".
            return this.Partial("_Form", new TrunkForm
            {
                AuthUsername = trunk.AuthUsername,
                CallerIDName = trunk.CallerIDName,
                CallerIDNumber = trunk.CallerIDNumber,
                Codecs = trunk.CodecList(),
                Enabled = trunk.Enabled,
                MatchAddresses = trunk.MatchAddresses,
                Name = trunk.Name,
                Register = trunk.Register,
                ServerHost = trunk.ServerHost,
                ServerPort = trunk.ServerPort,
                TrunkID = trunk.TrunkID,
                Username = trunk.Username,
            });
        }

        /// <summary>
        /// The five second poll: one badge per trunk, marked for an out-of-band swap so htmx drops
        /// each into the row it belongs to without touching the rest of the table.
        /// </summary>
        public PartialViewResult OnGetStatus()
        {
            var names = this.trunks.GetAll().Select(t => t.Name).ToList();
            var states = RegistrationStatus.ReadTrunks(this.Ami(), names);

            var badges = names
                .Select(name => StatusBadge.ForTrunk(name, states[name], outOfBand: true))
                .ToList();

            return this.Partial("_Status", badges);
        }

        /// <summary>The whole table, including current registration state so a refresh is not blank.</summary>
        public PartialViewResult OnGetTable()
        {
            var all = this.trunks.GetAll();
            var states = RegistrationStatus.ReadTrunks(this.Ami(), all.Select(t => t.Name));

            var rows = all
                .Select(trunk => new TrunkRow
                {
                    Enabled = trunk.Enabled,
                    Name = trunk.Name,
                    Register = trunk.Register,
                    Server = trunk.ServerPort == 5060 ? trunk.ServerHost : $"{trunk.ServerHost}:{trunk.ServerPort}",
                    State = states[trunk.Name],
                    TrunkID = trunk.TrunkID,
                })
                .ToList();

            return this.Partial("_Table", rows);
        }

        public IActionResult OnPostDelete(long trunkID)
        {
            var trunk = this.trunks.GetByID(trunkID);
            if (trunk == null)
                return this.NotFound();

            this.trunks.Delete(trunkID);

            Log.Info($"Trunk {trunk.Name} deleted by {this.User.Identity?.Name}");
            return this.Changed($"Trunk {trunk.Name} deleted.");
        }

        /// <summary>
        /// Creates or updates one trunk. Validation failures come back as the form again, with the
        /// repository's messages on it, which htmx swaps into the open modal.
        /// </summary>
        public IActionResult OnPostSave(TrunkForm form)
        {
            var isNew = form.TrunkID == 0;
            var trunk = isNew ? new Trunk() : this.trunks.GetByID(form.TrunkID);
            if (trunk == null)
                return this.NotFound();

            trunk.AuthUsername = Text(form.AuthUsername);
            trunk.CallerIDName = Text(form.CallerIDName);
            trunk.CallerIDNumber = Text(form.CallerIDNumber);
            trunk.Codecs = string.Join(",", form.Codecs);
            trunk.Enabled = form.Enabled;
            trunk.MatchAddresses = Text(form.MatchAddresses);
            trunk.Name = Text(form.Name);
            trunk.Register = form.Register;
            trunk.ServerHost = Text(form.ServerHost);
            trunk.ServerPort = form.ServerPort;
            trunk.Username = Text(form.Username);

            // The provider chooses the password, so an empty box means "the one we already have",
            // not "no password" (D41).
            var password = Text(form.Password);
            if (password.Length > 0)
                trunk.Password = password;

            try
            {
                if (isNew)
                    this.trunks.Insert(trunk);
                else
                    this.trunks.Update(trunk);
            }
            catch (ValidationFailedException ex)
            {
                form.Errors = ex.Errors.ToList();
                form.Password = "";
                return this.Partial("_Form", form);
            }

            Log.Info($"Trunk {trunk.Name} {(isNew ? "created" : "updated")} by {this.User.Identity?.Name}");
            return this.Changed($"Trunk {trunk.Name} saved.");
        }

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();

        private AmiSettings Ami() => AsteriskSettings.Ami(this.settings.GetAll());

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to.
        /// "trunksChanged" refreshes the table and the apply banner, "pbxToast" says what happened.
        /// </summary>
        private IActionResult Changed(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["trunksChanged"] = null,

                // The navbar's apply button polls, but it may as well know at once (D43).
                ["configChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }
    }
}
