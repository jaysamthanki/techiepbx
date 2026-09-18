using System.Globalization;
using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Pages.Shared;

namespace Techie.Pbx.Web.Pages.TimeConditions
{
    /// <summary>
    /// The time conditions page: open hours one way, closed hours another, holidays a third (F8).
    /// Built like the other list pages — a shell htmx fills, forms in the shared Bootstrap modal
    /// (D42), rows that open their own edit form (D48).
    ///
    /// What is different here is that one condition is one form, hours and holidays and all (D62),
    /// and those are lists an admin adds to. Each row is indexed by a key rather than by its
    /// position, so the page's few lines of JavaScript can add and remove rows without renumbering
    /// anything: ASP.NET Core reads the posted "hours.Index" and "holidays.Index" values.
    ///
    /// The timezone box in the header is here for the same reason the feature needs it: the hours
    /// are matched against Asterisk's own clock, and this setting records which zone that is meant
    /// to be (D65). The settings page can edit it too, as it can any setting (D67); it is repeated
    /// here because this is the screen where the value means something, next to the clock it
    /// describes.
    /// </summary>
    public class IndexModel : PageModel
    {
        /// <summary>What a row's field names are indexed by until the browser gives it a real key.</summary>
        private const string TemplateKey = "__key__";

        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        /// <summary>The timezone box in the page header. Filled in by OnGet.</summary>
        public TimezoneForm Timezone { get; private set; } = new();

        private readonly AnnouncementRepository announcements;
        private readonly ExtensionRepository extensions;
        private readonly IvrRepository ivrs;
        private readonly RingGroupRepository ringGroups;
        private readonly SettingsRepository settings;
        private readonly TimeConditionRepository timeConditions;

        public IndexModel()
        {
            this.announcements = new AnnouncementRepository(PbxDatabase.Current);
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.ivrs = new IvrRepository(PbxDatabase.Current);
            this.ringGroups = new RingGroupRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
            this.timeConditions = new TimeConditionRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
            this.Timezone = new TimezoneForm { Timezone = this.settings.Get(SettingsKeys.SystemTimezone) ?? "" };
        }

        /// <summary>
        /// The server's clock, ticking every five seconds in the timezone box. It is the clock
        /// Asterisk matches the open hours against (D65), so it belongs beside the zone name.
        /// Plain text, swapped into the clock span's innerHTML: no element of the card is
        /// ever replaced, so nothing around it can move.
        /// </summary>
        public ContentResult OnGetClock() =>
            this.Content(DateTimeOffset.Now.ToString("ddd MMM d HH:mm:ss yyyy zzz", CultureInfo.InvariantCulture), "text/plain");

        /// <summary>The create or edit form, which the page shows in the Bootstrap modal.</summary>
        public IActionResult OnGetForm(long? timeConditionID)
        {
            // A new condition starts with one empty open-hours row, because every condition needs
            // at least one: without any, it is closed at every hour of every day.
            if (timeConditionID is null or 0)
                return this.Partial("_Form", this.Fill(new TimeConditionForm { Hours = { new TimeConditionHoursForm() } }));

            var condition = this.timeConditions.GetByID(timeConditionID.Value);
            if (condition == null)
                return this.NotFound();

            return this.Partial("_Form", this.Fill(new TimeConditionForm
            {
                ClosedDestination = condition.ClosedDestinationKey(),
                Description = condition.Description,
                Enabled = condition.Enabled,
                HolidayDestination = condition.HolidayDestinationKey(),
                Holidays = condition.HolidayRules()
                    .Select(rule => new TimeConditionHolidayForm
                    {
                        Date = rule.HolidayDate,
                        Destination = rule.HasOverride ? rule.DestinationKey() : "",
                    })
                    .ToList(),
                Hours = condition.WeeklyRules()
                    .Select(rule => new TimeConditionHoursForm
                    {
                        Days = TimeConditionHoursForm.Weekdays
                            .Where(day => (rule.DaysMask & day.Bit) != 0)
                            .Select(day => day.Bit)
                            .ToList(),
                        EndTime = rule.EndTime,
                        StartTime = rule.StartTime,
                    })
                    .ToList(),
                Name = condition.Name,
                OpenDestination = condition.OpenDestinationKey(),
                PlayExtension = condition.PlayExtension,
                TimeConditionID = condition.TimeConditionID,
            }));
        }

        /// <summary>The whole table, by name.</summary>
        public PartialViewResult OnGetTable()
        {
            var all = this.timeConditions.GetAll();
            var choices = this.Catalog(all);

            var rows = all
                .Select(condition => new TimeConditionRow
                {
                    ClosedDestination = DestinationLabel.For(choices, condition.ToClosedDestination()),
                    Enabled = condition.Enabled,
                    HolidayDestination = DestinationLabel.For(choices, condition.ToHolidayDestination()),
                    Name = condition.Name,
                    OpenDestination = DestinationLabel.For(choices, condition.ToOpenDestination()),
                    PlayExtension = condition.PlayExtension.Length > 0 ? condition.PlayExtension : "—",
                    TimeConditionID = condition.TimeConditionID,
                })
                .ToList();

            return this.Partial("_Table", rows);
        }

        public IActionResult OnPostDelete(long timeConditionID)
        {
            var condition = this.timeConditions.GetByID(timeConditionID);
            if (condition == null)
                return this.NotFound();

            this.timeConditions.Delete(timeConditionID);

            Log.Info($"Time condition '{condition.Name}' deleted by {this.User.Identity?.Name}");
            return this.Changed($"Time condition '{condition.Name}' deleted.");
        }

        /// <summary>
        /// Creates or updates one condition, hours and holidays and all. Validation failures come
        /// back as the form again, with the repository's messages on it.
        /// </summary>
        public IActionResult OnPostSave(TimeConditionForm form)
        {
            var isNew = form.TimeConditionID == 0;
            var condition = isNew ? new TimeCondition() : this.timeConditions.GetByID(form.TimeConditionID);
            if (condition == null)
                return this.NotFound();

            Destination.TryParse(form.ClosedDestination, out var closed);
            Destination.TryParse(form.HolidayDestination, out var holiday);
            Destination.TryParse(form.OpenDestination, out var open);

            condition.ClosedDestinationType = closed.Type.ToString();
            condition.ClosedDestinationValue = closed.Value;
            condition.Description = Text(form.Description);
            condition.Enabled = form.Enabled;
            condition.HolidayDestinationType = holiday.Type.ToString();
            condition.HolidayDestinationValue = holiday.Value;
            condition.Name = Text(form.Name);
            condition.OpenDestinationType = open.Type.ToString();
            condition.OpenDestinationValue = open.Value;
            condition.PlayExtension = Text(form.PlayExtension);
            condition.Rules = Rules(form);

            try
            {
                if (isNew)
                    this.timeConditions.Insert(condition);
                else
                    this.timeConditions.Update(condition);
            }
            catch (ValidationFailedException ex)
            {
                form.Errors = ex.Errors.ToList();
                return this.Partial("_Form", this.Fill(form));
            }

            Log.Info($"Time condition '{condition.Name}' {(isNew ? "created" : "updated")} by {this.User.Identity?.Name}");
            return this.Changed($"Time condition '{condition.Name}' saved.");
        }

        /// <summary>
        /// The one setting this page edits (D74), which the settings page can also edit (D67). It
        /// decides how the hours on this page are read: the zone goes into every GotoIfTime the
        /// dialplan generates, so changing it changes a generated file and the repository raises
        /// the "apply is due" marker like it does for any other setting.
        ///
        /// The value is checked with the same rules the repository applies, so a zone it would
        /// reject is reported here in place rather than thrown out of the save. The form posts a
        /// zone chosen from the server's own list (D75); blank can now only come from something
        /// that is not our form, and still means "back to the default".
        /// </summary>
        public IActionResult OnPostTimezone(string? timezone)
        {
            var value = Text(timezone);
            var form = new TimezoneForm { Timezone = value };

            form.Errors = SettingsValidation.Errors(SettingsKeys.SystemTimezone, value);
            if (form.Errors.Count > 0)
                return this.Partial("_Timezone", form);

            // Blank puts it back to the built-in default rather than storing an empty string, so
            // clearing the box and clearing the setting are the same thing.
            if (value.Length == 0)
                this.settings.Delete(SettingsKeys.SystemTimezone);
            else
                this.settings.Set(SettingsKeys.SystemTimezone, value);

            Log.Info($"System timezone set to '{(value.Length == 0 ? AsteriskSettings.DefaultTimezone : value)}' by {this.User.Identity?.Name}");
            this.Announce($"Timezone recorded as {(value.Length == 0 ? AsteriskSettings.DefaultTimezone : value)}.");

            return this.Partial("_Timezone", form);
        }

        /// <summary>The picker for one holiday row's override, which is optional by definition.</summary>
        private static DestinationSelect HolidayChoices(List<DestinationChoice> choices, string key, string? selected) =>
            new()
            {
                Choices = choices,
                ElementID = $"tc-holiday-{key}",
                Name = $"holidays[{key}].destination",
                Placeholder = "Use the holiday destination",
                Required = false,
                SelectedKey = string.IsNullOrEmpty(selected) ? null : selected,
            };

        /// <summary>
        /// The posted hours and holidays as rules to save. A row the admin added and left entirely
        /// blank is dropped rather than stored empty, the way an unused IVR key is (D59); a row
        /// that is half filled in is kept, so that validation can say what is missing.
        /// </summary>
        private static List<TimeConditionRule> Rules(TimeConditionForm form)
        {
            var rules = new List<TimeConditionRule>();
            var order = 0;

            foreach (var row in form.Hours.Where(r => !r.IsBlank()))
            {
                rules.Add(new TimeConditionRule
                {
                    DaysMask = row.DaysMask(),
                    EndTime = Text(row.EndTime),
                    Kind = TimeConditionRuleKind.Weekly,
                    SortOrder = order++,
                    StartTime = Text(row.StartTime),
                });
            }

            order = 0;

            foreach (var row in form.Holidays.Where(r => !r.IsBlank()))
            {
                var rule = new TimeConditionRule
                {
                    HolidayDate = Text(row.Date),
                    Kind = TimeConditionRuleKind.Holiday,
                    SortOrder = order++,
                };

                SetOverride(rule, Text(row.Destination));
                rules.Add(rule);
            }

            return rules;
        }

        /// <summary>
        /// A holiday row's own destination, when it has one. What the picker posted is kept even
        /// when it does not read back as a destination, so validation can say so rather than the
        /// row quietly losing its override.
        /// </summary>
        private static void SetOverride(TimeConditionRule rule, string key)
        {
            if (key.Length == 0)
                return;

            if (Destination.TryParse(key, out var destination))
            {
                rule.DestinationType = destination.Type.ToString();
                rule.DestinationValue = destination.Value;
                return;
            }

            var separator = key.IndexOf(':');
            rule.DestinationType = separator < 0 ? key : key[..separator];
            rule.DestinationValue = separator < 0 ? "" : key[(separator + 1)..];
        }

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();

        /// <summary>
        /// The events a change on this page raises: "timeConditionsChanged" refreshes the table,
        /// "configChanged" wakes the navbar's apply button (D43), "pbxToast" says what happened.
        /// </summary>
        private void Announce(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["timeConditionsChanged"] = null,
                ["configChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
        }

        /// <summary>Every place a call can be sent, given the conditions already loaded (D35).</summary>
        private List<DestinationChoice> Catalog(List<TimeCondition> all) =>
            DestinationCatalog.All(
                this.extensions.GetAll(), this.ringGroups.GetAll(), this.announcements.GetAll(), this.ivrs.GetAll(), all);

        /// <summary>The answer to a change: no content to swap, and events for the page to react to.</summary>
        private IActionResult Changed(string message)
        {
            this.Announce(message);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// The lists the form cannot know for itself: every place a call can be sent, for the three
        /// cases and for each holiday's override alike (D35), and the keys the rows are indexed by.
        ///
        /// The condition being edited is left off its own pickers, because a condition cannot send
        /// a call back to itself (D63). Every <i>other</i> condition is on them: one condition
        /// handing over to another is how "closed" gets a second set of hours (D62).
        /// </summary>
        private TimeConditionForm Fill(TimeConditionForm form)
        {
            var all = this.timeConditions.GetAll();
            var stored = all.FirstOrDefault(t => t.TimeConditionID == form.TimeConditionID);

            var choices = this.Catalog(all)
                .Where(c => stored == null ||
                    c.Destination.Type != DestinationType.TimeCondition ||
                    !string.Equals(c.Destination.Value, stored.PlayExtension, StringComparison.Ordinal))
                .ToList();

            form.ClosedDestinationChoices = new DestinationSelect
            {
                Choices = choices,
                ElementID = "tc-closed-destination",
                Name = "closedDestination",
                SelectedKey = string.IsNullOrEmpty(form.ClosedDestination) ? null : form.ClosedDestination,
            };

            form.HolidayDestinationChoices = new DestinationSelect
            {
                Choices = choices,
                ElementID = "tc-holiday-destination",
                Name = "holidayDestination",
                SelectedKey = string.IsNullOrEmpty(form.HolidayDestination) ? null : form.HolidayDestination,
            };

            form.OpenDestinationChoices = new DestinationSelect
            {
                Choices = choices,
                ElementID = "tc-open-destination",
                Name = "openDestination",
                SelectedKey = string.IsNullOrEmpty(form.OpenDestination) ? null : form.OpenDestination,
            };

            for (var index = 0; index < form.Holidays.Count; index++)
            {
                var row = form.Holidays[index];

                row.Key = index.ToString(CultureInfo.InvariantCulture);
                row.Choices = HolidayChoices(choices, row.Key, row.Destination);
            }

            for (var index = 0; index < form.Hours.Count; index++)
                form.Hours[index].Key = index.ToString(CultureInfo.InvariantCulture);

            // The rows the Add buttons copy. Their keys are the placeholder the page's JavaScript
            // swaps for one nothing else is using, and they never collide with the numbers above.
            form.HolidayTemplate = new TimeConditionHolidayForm
            {
                Choices = HolidayChoices(choices, TemplateKey, null),
                Key = TemplateKey,
            };

            form.HoursTemplate = new TimeConditionHoursForm { Key = TemplateKey };

            return form;
        }
    }
}
