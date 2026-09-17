using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Pages.Shared;

namespace Techie.Pbx.Web.Pages.Ivrs
{
    /// <summary>
    /// The IVRs page: an auto attendant — a greeting, a digit map, and somewhere for a caller who
    /// pressed nothing usable to end up (F6). Built like the other list pages — a shell htmx fills,
    /// forms in the shared Bootstrap modal (D42), rows that open their own edit form (D48).
    ///
    /// What is different here is the digit map: the form carries a row for every key a caller could
    /// press, so adding and removing keys is choosing and clearing destinations, with no JavaScript
    /// of ours in it at all (D59).
    /// </summary>
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly AnnouncementRepository announcements;
        private readonly ExtensionRepository extensions;
        private readonly IvrRepository ivrs;
        private readonly RingGroupRepository ringGroups;

        public IndexModel()
        {
            this.announcements = new AnnouncementRepository(PbxDatabase.Current);
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.ivrs = new IvrRepository(PbxDatabase.Current);
            this.ringGroups = new RingGroupRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>The create or edit form, which the page shows in the Bootstrap modal.</summary>
        public IActionResult OnGetForm(long? ivrID)
        {
            if (ivrID is null or 0)
                return this.Partial("_Form", this.Fill(new IvrForm()));

            var ivr = this.ivrs.GetByID(ivrID.Value);
            if (ivr == null)
                return this.NotFound();

            return this.Partial("_Form", this.Fill(new IvrForm
            {
                AnnouncementID = ivr.AnnouncementID,
                Description = ivr.Description,
                Destination = ivr.DestinationKey(),
                EnableDirectDial = ivr.EnableDirectDial,
                Enabled = ivr.Enabled,
                IvrID = ivr.IvrID,
                Keys = ivr.Entries
                    .Select(entry => new IvrKeyForm { Destination = entry.DestinationKey(), Digit = entry.Digit })
                    .ToList(),
                Name = ivr.Name,
                PlayExtension = ivr.PlayExtension,
                Retries = ivr.Retries,
                TimeoutSeconds = ivr.TimeoutSeconds,
            }));
        }

        /// <summary>The whole table, by name.</summary>
        public PartialViewResult OnGetTable()
        {
            var allAnnouncements = this.announcements.GetAll();

            var rows = this.ivrs.GetAll()
                .Select(ivr =>
                {
                    var greeting = allAnnouncements.FirstOrDefault(a => a.AnnouncementID == ivr.AnnouncementID);

                    return new IvrRow
                    {
                        DirectDial = ivr.EnableDirectDial,
                        Enabled = ivr.Enabled,
                        Greeting = greeting?.Name ?? "(announcement gone)",
                        GreetingUsable = ivr.GreetingIn(allAnnouncements) != null,
                        IvrID = ivr.IvrID,
                        Name = ivr.Name,
                        PlayExtension = ivr.PlayExtension.Length > 0 ? ivr.PlayExtension : "—",
                        Retries = ivr.Retries,
                        TimeoutSeconds = ivr.TimeoutSeconds,
                    };
                })
                .ToList();

            return this.Partial("_Table", rows);
        }

        public IActionResult OnPostDelete(long ivrID)
        {
            var ivr = this.ivrs.GetByID(ivrID);
            if (ivr == null)
                return this.NotFound();

            this.ivrs.Delete(ivrID);

            Log.Info($"IVR '{ivr.Name}' deleted by {this.User.Identity?.Name}");
            return this.Changed($"IVR '{ivr.Name}' deleted.");
        }

        /// <summary>
        /// Creates or updates one menu, digit map and all. Validation failures come back as the
        /// form again, with the repository's messages on it.
        /// </summary>
        public IActionResult OnPostSave(IvrForm form)
        {
            var isNew = form.IvrID == 0;
            var ivr = isNew ? new Ivr() : this.ivrs.GetByID(form.IvrID);
            if (ivr == null)
                return this.NotFound();

            Destination.TryParse(form.Destination, out var destination);

            ivr.AnnouncementID = form.AnnouncementID;
            ivr.Description = Text(form.Description);
            ivr.DestinationType = destination.Type.ToString();
            ivr.DestinationValue = destination.Value;
            ivr.EnableDirectDial = form.EnableDirectDial;
            ivr.Enabled = form.Enabled;
            ivr.Entries = Entries(form);
            ivr.Name = Text(form.Name);
            ivr.PlayExtension = Text(form.PlayExtension);
            ivr.Retries = form.Retries;
            ivr.TimeoutSeconds = form.TimeoutSeconds;

            try
            {
                if (isNew)
                    this.ivrs.Insert(ivr);
                else
                    this.ivrs.Update(ivr);
            }
            catch (ValidationFailedException ex)
            {
                form.Errors = ex.Errors.ToList();
                return this.Partial("_Form", this.Fill(form));
            }

            Log.Info($"IVR '{ivr.Name}' {(isNew ? "created" : "updated")} by {this.User.Identity?.Name}");
            return this.Changed($"IVR '{ivr.Name}' saved.");
        }

        /// <summary>
        /// The posted digit map as entries to save. A row left on "Not used" is dropped rather than
        /// stored empty: a key with no destination is a key the menu does not have (D59).
        /// </summary>
        private static List<IvrEntry> Entries(IvrForm form)
        {
            var entries = new List<IvrEntry>();

            foreach (var key in form.Keys)
            {
                var digit = Text(key.Digit);

                if (!IvrEntry.IsValidDigit(digit) || !Destination.TryParse(key.Destination, out var destination))
                    continue;

                entries.Add(new IvrEntry
                {
                    DestinationType = destination.Type.ToString(),
                    DestinationValue = destination.Value,
                    Digit = digit,
                });
            }

            return entries.OrderBy(e => IvrEntry.Rank(e.Digit)).ToList();
        }

        /// <summary>
        /// A row for every key a caller could press, in keypad order, carrying whatever the posted
        /// or stored map had for it. Fixed rows rather than add and remove buttons: there are only
        /// twelve keys, they never change, and this way the editor needs no JavaScript.
        /// </summary>
        private static List<IvrKeyForm> KeyRows(List<IvrKeyForm> chosen, List<DestinationChoice> choices)
        {
            var byDigit = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var key in chosen.Where(k => !string.IsNullOrEmpty(k.Digit)))
                byDigit[key.Digit!] = key.Destination ?? "";

            return IvrEntry.Keypad
                .Select((digit, index) =>
                {
                    var destination = byDigit.TryGetValue(digit.ToString(), out var stored) ? stored : "";

                    return new IvrKeyForm
                    {
                        Choices = new DestinationSelect
                        {
                            Choices = choices,
                            ElementID = $"ivr-key-{index}",
                            Name = $"keys[{index}].destination",
                            Placeholder = "Not used",
                            Required = false,
                            SelectedKey = destination.Length == 0 ? null : destination,
                        },
                        Destination = destination,
                        Digit = digit.ToString(),
                    };
                })
                .ToList();
        }

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to.
        /// "ivrsChanged" refreshes the table, "configChanged" wakes the navbar's apply button
        /// (D43), "pbxToast" says what happened.
        /// </summary>
        private IActionResult Changed(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["ivrsChanged"] = null,
                ["configChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// The lists the form cannot know for itself: the announcements a greeting can come from
        /// (D58), and every place a call can be sent — for the final destination and for each key
        /// of the digit map alike (D35).
        ///
        /// The menu being edited is in that catalog along with the others, because a key pointing
        /// at its own menu is "press 9 to hear this again" rather than a loop (D59).
        /// </summary>
        private IvrForm Fill(IvrForm form)
        {
            var allAnnouncements = this.announcements.GetAll();
            var choices = DestinationCatalog.All(
                this.extensions.GetAll(), this.ringGroups.GetAll(), allAnnouncements, this.ivrs.GetAll());

            form.DestinationChoices = new DestinationSelect
            {
                Choices = choices,
                ElementID = "ivr-destination",
                Name = "destination",
                SelectedKey = string.IsNullOrEmpty(form.Destination) ? null : form.Destination,
            };

            // Anything with audio, including a switched-off announcement: it is offered so that the
            // greeting an IVR already points at is still shown, and the save says plainly why a
            // switched-off one cannot be used.
            form.Greetings = allAnnouncements
                .Where(a => a.HasAudio)
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            form.GreetingMissing = form.AnnouncementID > 0
                && !form.Greetings.Any(a => a.AnnouncementID == form.AnnouncementID);

            form.Keys = KeyRows(form.Keys, choices);

            return form;
        }
    }
}
