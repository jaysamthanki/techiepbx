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
using Techie.Pbx.Web.Pages.TimeConditions;

namespace Techie.Pbx.Web.Pages.CallFlowControls
{
    /// <summary>
    /// The call flow controls page: day/night switches a phone flips by dialling a code (F9). Built
    /// like the other list pages — a shell htmx fills, forms in the shared Bootstrap modal (D42),
    /// rows that open their own edit form (D48).
    ///
    /// The state badge is read from astdb over AMI when the table is drawn, and is read-only: a
    /// switch is flipped from a phone, which also lights the key lamps. Flipping it from here would
    /// have to set the lamp too, which a plain DBPut does not.
    /// </summary>
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly AnnouncementRepository announcements;
        private readonly CallFlowControlRepository callFlowControls;
        private readonly ExtensionRepository extensions;
        private readonly IvrRepository ivrs;
        private readonly RingGroupRepository ringGroups;
        private readonly SettingsRepository settings;
        private readonly TimeConditionRepository timeConditions;

        public IndexModel()
        {
            this.announcements = new AnnouncementRepository(PbxDatabase.Current);
            this.callFlowControls = new CallFlowControlRepository(PbxDatabase.Current);
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.ivrs = new IvrRepository(PbxDatabase.Current);
            this.ringGroups = new RingGroupRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
            this.timeConditions = new TimeConditionRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>The create or edit form, which the page shows in the Bootstrap modal.</summary>
        public IActionResult OnGetForm(long? callFlowControlID)
        {
            if (callFlowControlID is null or 0)
                return this.Partial("_Form", this.Fill(new CallFlowControlForm()));

            var control = this.callFlowControls.GetByID(callFlowControlID.Value);
            if (control == null)
                return this.NotFound();

            return this.Partial("_Form", this.Fill(new CallFlowControlForm
            {
                CallFlowControlID = control.CallFlowControlID,
                FeatureCode = control.FeatureCode,
                Name = control.Name,
                NormalDestination = control.NormalDestinationKey(),
                OverrideDestination = control.OverrideDestinationKey(),
            }));
        }

        /// <summary>The whole table, by name, with each switch's state as Asterisk has it now.</summary>
        public PartialViewResult OnGetTable()
        {
            var all = this.callFlowControls.GetAll();
            var choices = this.Catalog(all);
            var states = CallFlowControlStatus.Read(AsteriskSettings.Ami(this.settings.GetAll()), all);

            var rows = all
                .Select(control => new CallFlowControlRow
                {
                    CallFlowControlID = control.CallFlowControlID,
                    FeatureCode = control.FeatureCode,
                    Name = control.Name,
                    Normal = DestinationLabel.For(choices, control.ToNormalDestination()),
                    Override = DestinationLabel.For(choices, control.ToOverrideDestination()),
                    State = states.TryGetValue(control.CallFlowControlID, out var state) ? state : CallFlowState.Unknown,
                })
                .ToList();

            return this.Partial("_Table", rows);
        }

        public IActionResult OnPostDelete(long callFlowControlID)
        {
            var control = this.callFlowControls.GetByID(callFlowControlID);
            if (control == null)
                return this.NotFound();

            this.callFlowControls.Delete(callFlowControlID);

            Log.Info($"Call flow control '{control.Name}' ({control.FeatureCode}) deleted by {this.User.Identity?.Name}");
            return this.Changed($"Call flow control {control.Name} deleted.");
        }

        /// <summary>
        /// Creates or updates one switch. Validation failures come back as the form again, with the
        /// repository's messages on it.
        /// </summary>
        public IActionResult OnPostSave(CallFlowControlForm form)
        {
            var isNew = form.CallFlowControlID == 0;
            var control = isNew ? new CallFlowControl() : this.callFlowControls.GetByID(form.CallFlowControlID);
            if (control == null)
                return this.NotFound();

            Destination.TryParse(form.NormalDestination, out var normal);
            Destination.TryParse(form.OverrideDestination, out var over);

            control.FeatureCode = Text(form.FeatureCode);
            control.Name = Text(form.Name);
            control.NormalDestinationType = normal.Type.ToString();
            control.NormalDestinationValue = normal.Value;
            control.OverrideDestinationType = over.Type.ToString();
            control.OverrideDestinationValue = over.Value;

            try
            {
                if (isNew)
                    this.callFlowControls.Insert(control);
                else
                    this.callFlowControls.Update(control);
            }
            catch (ValidationFailedException ex)
            {
                form.Errors = ex.Errors.ToList();
                return this.Partial("_Form", this.Fill(form));
            }

            Log.Info($"Call flow control '{control.Name}' ({control.FeatureCode}) {(isNew ? "created" : "updated")} by {this.User.Identity?.Name}");
            return this.Changed($"Call flow control {control.Name} saved.");
        }

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();

        /// <summary>Every place a call can be sent, given the switches already loaded (D35).</summary>
        private List<DestinationChoice> Catalog(List<CallFlowControl> all) =>
            DestinationCatalog.All(
                this.extensions.GetAll(), this.ringGroups.GetAll(), this.announcements.GetAll(), this.ivrs.GetAll(),
                this.timeConditions.GetAll(), all);

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to.
        /// "callFlowControlsChanged" refreshes the table, "configChanged" wakes the navbar's apply
        /// button (D43), "pbxToast" says what happened.
        /// </summary>
        private IActionResult Changed(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["callFlowControlsChanged"] = null,
                ["configChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// The two pickers: the whole catalog, less the switch being edited (D136). Every other
        /// switch stays on them, because one switch handing to another is how they chain.
        /// </summary>
        private CallFlowControlForm Fill(CallFlowControlForm form)
        {
            var all = this.callFlowControls.GetAll();
            var stored = all.FirstOrDefault(c => c.CallFlowControlID == form.CallFlowControlID);
            var choices = DestinationCatalog.Except(this.Catalog(all), stored?.ToDestination());

            form.NormalDestinationChoices = new DestinationSelect
            {
                Choices = choices,
                ElementID = "cfc-normal",
                Name = "normalDestination",
                SelectedKey = string.IsNullOrEmpty(form.NormalDestination) ? null : form.NormalDestination,
            };

            form.OverrideDestinationChoices = new DestinationSelect
            {
                Choices = choices,
                ElementID = "cfc-override",
                Name = "overrideDestination",
                SelectedKey = string.IsNullOrEmpty(form.OverrideDestination) ? null : form.OverrideDestination,
            };

            return form;
        }
    }
}
