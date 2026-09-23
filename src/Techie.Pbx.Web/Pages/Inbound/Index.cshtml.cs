using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Pages.Shared;

namespace Techie.Pbx.Web.Pages.Inbound
{
    /// <summary>
    /// The inbound routes page: which number arriving on which trunk goes where. The first page to
    /// use the shared destination picker (D35), which is what "where" means everywhere.
    /// </summary>
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly AnnouncementRepository announcements;
        private readonly CallFlowControlRepository callFlowControls;
        private readonly ExtensionRepository extensions;
        private readonly InboundRouteRepository inbound;
        private readonly IvrRepository ivrs;
        private readonly MohClassRepository mohClasses;
        private readonly RingGroupRepository ringGroups;
        private readonly TimeConditionRepository timeConditions;
        private readonly TrunkRepository trunks;

        public IndexModel()
        {
            this.announcements = new AnnouncementRepository(PbxDatabase.Current);
            this.callFlowControls = new CallFlowControlRepository(PbxDatabase.Current);
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.inbound = new InboundRouteRepository(PbxDatabase.Current);
            this.ivrs = new IvrRepository(PbxDatabase.Current);
            this.mohClasses = new MohClassRepository(PbxDatabase.Current);
            this.ringGroups = new RingGroupRepository(PbxDatabase.Current);
            this.timeConditions = new TimeConditionRepository(PbxDatabase.Current);
            this.trunks = new TrunkRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>The create or edit form, which the page shows in the Bootstrap modal.</summary>
        public IActionResult OnGetForm(long? inboundRouteID)
        {
            if (inboundRouteID is null or 0)
                return this.Partial("_Form", this.Fill(new InboundRouteForm()));

            var route = this.inbound.GetByID(inboundRouteID.Value);
            if (route == null)
                return this.NotFound();

            return this.Partial("_Form", this.Fill(new InboundRouteForm
            {
                CatchAll = route.CatchAll,
                Destination = route.DestinationKey(),
                Description = route.Description,
                DID = route.DID,
                Enabled = route.Enabled,
                InboundRouteID = route.InboundRouteID,
                MohClassID = route.MohClassID,
                TrunkID = route.TrunkID,
            }));
        }

        /// <summary>The whole table, in the order the routes read best.</summary>
        public PartialViewResult OnGetTable()
        {
            var allExtensions = this.extensions.GetAll();
            var allGroups = this.ringGroups.GetAll();
            var allAnnouncements = this.announcements.GetAll();
            var allIvrs = this.ivrs.GetAll();
            var allConditions = this.timeConditions.GetAll();
            var allControls = this.callFlowControls.GetAll();
            var allTrunks = this.trunks.GetAll();

            var rows = this.inbound.GetAll()
                .Select(route =>
                {
                    var trunk = allTrunks.FirstOrDefault(t => t.TrunkID == route.TrunkID);
                    var choice = DestinationCatalog.Find(
                        allExtensions, allGroups, allAnnouncements, allIvrs, allConditions, allControls, route.ToDestination());

                    return new InboundRouteRow
                    {
                        Description = route.Description,
                        Destination = choice?.Label ?? $"{route.DestinationKey()} (gone)",
                        DestinationUsable = choice != null,
                        DID = route.CatchAll ? "Any other number" : route.DID,
                        Enabled = route.Enabled,
                        InboundRouteID = route.InboundRouteID,
                        Trunk = trunk?.Name ?? "(trunk deleted)",
                        TrunkUsable = trunk is { Enabled: true },
                    };
                })
                .ToList();

            return this.Partial("_Table", rows);
        }

        public IActionResult OnPostDelete(long inboundRouteID)
        {
            var route = this.inbound.GetByID(inboundRouteID);
            if (route == null)
                return this.NotFound();

            this.inbound.Delete(inboundRouteID);

            Log.Info($"Inbound route {Describe(route)} deleted by {this.User.Identity?.Name}");
            return this.Changed($"Inbound route for {Describe(route)} deleted.");
        }

        /// <summary>
        /// Creates or updates one route. Validation failures come back as the form again, with the
        /// repository's messages on it.
        /// </summary>
        public IActionResult OnPostSave(InboundRouteForm form)
        {
            var isNew = form.InboundRouteID == 0;
            var route = isNew ? new InboundRoute() : this.inbound.GetByID(form.InboundRouteID);
            if (route == null)
                return this.NotFound();

            // A catch-all has no DID of its own, whatever was left in the box.
            Destination.TryParse(form.Destination, out var destination);

            route.CatchAll = form.CatchAll;
            route.Description = Text(form.Description);
            route.DestinationType = destination.Type.ToString();
            route.DestinationValue = destination.Value;
            route.DID = form.CatchAll ? "" : Text(form.DID);
            route.Enabled = form.Enabled;
            route.MohClassID = form.MohClassID;
            route.TrunkID = form.TrunkID;

            try
            {
                if (isNew)
                    this.inbound.Insert(route);
                else
                    this.inbound.Update(route);
            }
            catch (ValidationFailedException ex)
            {
                form.Errors = ex.Errors.ToList();
                return this.Partial("_Form", this.Fill(form));
            }

            Log.Info($"Inbound route {Describe(route)} {(isNew ? "created" : "updated")} by {this.User.Identity?.Name}");
            return this.Changed($"Inbound route for {Describe(route)} saved.");
        }

        /// <summary>What to call a route in a log line or a toast.</summary>
        private static string Describe(InboundRoute route) => route.CatchAll ? "any other number" : route.DID;

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to.
        /// "inboundChanged" refreshes the table, "configChanged" wakes the navbar's apply button
        /// (D43), "pbxToast" says what happened.
        /// </summary>
        private IActionResult Changed(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["inboundChanged"] = null,
                ["configChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// The lists the form cannot know for itself: the trunks calls can arrive on, every place a
        /// call can be sent (D35), and the music on hold classes a held caller could be played
        /// (D122 amended).
        ///
        /// Every source the catalog knows, which is what the repository has always validated a
        /// saved route against. Ring groups, announcements, IVRs and time conditions belong here
        /// as much as extensions do: a caller from outside usually wants the clock checked and the
        /// menu played before any phone rings at all (D35 amendment).
        /// </summary>
        private InboundRouteForm Fill(InboundRouteForm form)
        {
            form.MohClasses = this.mohClasses.GetAll();
            form.Trunks = this.trunks.GetAll().Where(t => t.Enabled).ToList();
            form.DestinationChoices = new DestinationSelect
            {
                Choices = DestinationCatalog.All(
                    this.extensions.GetAll(),
                    this.ringGroups.GetAll(),
                    this.announcements.GetAll(),
                    this.ivrs.GetAll(),
                    this.timeConditions.GetAll(),
                    this.callFlowControls.GetAll()),
                ElementID = "route-destination",
                Name = "destination",
                SelectedKey = string.IsNullOrEmpty(form.Destination) ? null : form.Destination,
            };

            return form;
        }
    }
}
