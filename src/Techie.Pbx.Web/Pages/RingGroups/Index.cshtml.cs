using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Pages.Shared;

namespace Techie.Pbx.Web.Pages.RingGroups
{
    /// <summary>
    /// The ring groups page: a number that rings several phones, and where the call goes when
    /// nobody picks up (F3). Built like the other list pages — a shell htmx fills, forms in the
    /// shared Bootstrap modal (D42), rows that open their own edit form (D48).
    /// </summary>
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly ExtensionRepository extensions;
        private readonly RingGroupRepository ringGroups;

        public IndexModel()
        {
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.ringGroups = new RingGroupRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>The create or edit form, which the page shows in the Bootstrap modal.</summary>
        public IActionResult OnGetForm(long? ringGroupID)
        {
            if (ringGroupID is null or 0)
                return this.Partial("_Form", this.Fill(new RingGroupForm()));

            var group = this.ringGroups.GetByID(ringGroupID.Value);
            if (group == null)
                return this.NotFound();

            return this.Partial("_Form", this.Fill(new RingGroupForm
            {
                CallerIDPrefix = group.CallerIDPrefix,
                Destination = group.DestinationKey(),
                Enabled = group.Enabled,
                Members = group.MemberList(),
                Name = group.Name,
                Number = group.Number,
                RingGroupID = group.RingGroupID,
                RingSeconds = group.RingSeconds,
                Strategy = group.Strategy,
            }));
        }

        /// <summary>The whole table, by number.</summary>
        public PartialViewResult OnGetTable()
        {
            var allExtensions = this.extensions.GetAll();
            var allGroups = this.ringGroups.GetAll();

            var rows = allGroups
                .Select(group =>
                {
                    var choice = DestinationCatalog.Find(allExtensions, allGroups, group.ToDestination());

                    return new RingGroupRow
                    {
                        Destination = choice?.Label ?? $"{group.DestinationKey()} (gone)",
                        DestinationUsable = choice != null,
                        Enabled = group.Enabled,
                        Members = string.Join(", ", group.MemberList()),
                        Name = group.Name,
                        Number = group.Number,
                        RingGroupID = group.RingGroupID,
                        RingSeconds = group.RingSeconds,
                        Strategy = group.ToStrategy() == RingStrategy.All ? "Ring all" : "Hunt",
                    };
                })
                .ToList();

            return this.Partial("_Table", rows);
        }

        public IActionResult OnPostDelete(long ringGroupID)
        {
            var group = this.ringGroups.GetByID(ringGroupID);
            if (group == null)
                return this.NotFound();

            this.ringGroups.Delete(ringGroupID);

            Log.Info($"Ring group {group.Number} deleted by {this.User.Identity?.Name}");
            return this.Changed($"Ring group {group.Number} deleted.");
        }

        /// <summary>
        /// Creates or updates one group. Validation failures come back as the form again, with the
        /// repository's messages on it.
        /// </summary>
        public IActionResult OnPostSave(RingGroupForm form)
        {
            var isNew = form.RingGroupID == 0;
            var group = isNew ? new RingGroup() : this.ringGroups.GetByID(form.RingGroupID);
            if (group == null)
                return this.NotFound();

            Destination.TryParse(form.Destination, out var destination);

            group.CallerIDPrefix = Text(form.CallerIDPrefix);
            group.DestinationType = destination.Type.ToString();
            group.DestinationValue = destination.Value;
            group.Enabled = form.Enabled;
            group.Members = string.Join(",", form.Members);
            group.Name = Text(form.Name);
            group.Number = Text(form.Number);
            group.RingSeconds = form.RingSeconds;
            group.Strategy = Text(form.Strategy);

            try
            {
                if (isNew)
                    this.ringGroups.Insert(group);
                else
                    this.ringGroups.Update(group);
            }
            catch (ValidationFailedException ex)
            {
                form.Errors = ex.Errors.ToList();
                return this.Partial("_Form", this.Fill(form));
            }

            Log.Info($"Ring group {group.Number} {(isNew ? "created" : "updated")} by {this.User.Identity?.Name}");
            return this.Changed($"Ring group {group.Number} saved.");
        }

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to.
        /// "ringGroupsChanged" refreshes the table, "configChanged" wakes the navbar's apply
        /// button (D43), "pbxToast" says what happened.
        /// </summary>
        private IActionResult Changed(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["ringGroupsChanged"] = null,
                ["configChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// The lists the form cannot know for itself: the extensions that can be members, and
        /// every place an unanswered call can be sent — including the other ring groups (D54).
        /// </summary>
        private RingGroupForm Fill(RingGroupForm form)
        {
            var allExtensions = this.extensions.GetAll();

            form.Extensions = allExtensions.Where(e => e.Enabled).ToList();
            form.DestinationChoices = new DestinationSelect
            {
                Choices = DestinationCatalog.All(allExtensions, this.ringGroups.GetAll()),
                ElementID = "group-destination",
                Name = "destination",
                SelectedKey = string.IsNullOrEmpty(form.Destination) ? null : form.Destination,
            };

            return form;
        }
    }
}
