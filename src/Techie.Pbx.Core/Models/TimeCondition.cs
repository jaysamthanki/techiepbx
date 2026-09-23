using System.Globalization;
using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// "Open hours go here, closed hours go there, holidays go somewhere else" — one condition,
    /// one form, three destinations (D63). There is deliberately no time <i>group</i> entity to
    /// reference: humans think in cases, not in referenced tables of time ranges.
    ///
    /// The rules are the when: any number of weekly open windows, and any number of holiday dates
    /// that win over them. The optional play extension is what makes the condition dialable and
    /// what a destination points at, exactly as an announcement's and an IVR's do (D57, D59).
    ///
    /// <b>Time is the server's own.</b> <c>GotoIfTime</c> is matched against Asterisk's local
    /// clock, so what makes these hours mean what an admin expects is the machine's timezone, not
    /// anything stored here. <c>SettingsKeys.SystemTimezone</c> records which zone that is meant to
    /// be, and the renderer writes it into the dialplan as a comment (D65).
    /// </summary>
    public partial class TimeCondition
    {
        /// <summary>The kind of destination for a call outside the open hours, by name (D35).</summary>
        public string ClosedDestinationType { get; set; } = Models.DestinationType.Hangup.ToString();

        /// <summary>What that destination points at. Empty for Hangup.</summary>
        public string ClosedDestinationValue { get; set; } = "";

        /// <summary>What the condition is for, in an admin's words. Ends up as a dialplan comment.</summary>
        public string Description { get; set; } = "";

        public bool Enabled { get; set; } = true;

        /// <summary>Where a call on one of the holiday dates goes, unless that date overrides it.</summary>
        public string HolidayDestinationType { get; set; } = Models.DestinationType.Hangup.ToString();

        /// <summary>What that destination points at. Empty for Hangup.</summary>
        public string HolidayDestinationValue { get; set; } = "";

        /// <summary>What the condition is called. Unique, and what an admin picks it by.</summary>
        public string Name { get; set; } = "";

        /// <summary>Where a call inside the open hours goes.</summary>
        public string OpenDestinationType { get; set; } = Models.DestinationType.Hangup.ToString();

        /// <summary>What that destination points at. Empty for Hangup.</summary>
        public string OpenDestinationValue { get; set; } = "";

        /// <summary>
        /// The number to dial to test the condition, or empty for none. Optional, and without it
        /// the condition has no dialplan entry and cannot be a destination — the same rule, for the
        /// same reason, as an announcement's and an IVR's (D57, D59).
        /// </summary>
        public string PlayExtension { get; set; } = "";

        /// <summary>
        /// When the site is open, and which dates are holidays. Not columns: these are rows in
        /// <c>TimeConditionRules</c>, loaded and written with the condition because they are only
        /// ever read together.
        /// </summary>
        public List<TimeConditionRule> Rules { get; set; } = new();

        public long TimeConditionID { get; set; }

        /// <summary>
        /// The dialplan context this condition's checks live in. One per condition, named by ID for
        /// the same reason an IVR's is (D59): the ID never changes and a name is free text.
        /// </summary>
        public string Context => "tc-" + this.TimeConditionID.ToString(CultureInfo.InvariantCulture);

        /// <summary>Whether a call can be sent here: switched on, and with a number to send it to.</summary>
        public bool IsPlayable => this.Enabled && this.PlayExtension.Length > 0;

        /// <summary>The two closed columns as the one string the picker posts (D35).</summary>
        public string ClosedDestinationKey() =>
            this.ClosedDestinationValue.Length == 0
                ? this.ClosedDestinationType
                : $"{this.ClosedDestinationType}:{this.ClosedDestinationValue}";

        /// <summary>The two holiday columns as the one string the picker posts (D35).</summary>
        public string HolidayDestinationKey() =>
            this.HolidayDestinationValue.Length == 0
                ? this.HolidayDestinationType
                : $"{this.HolidayDestinationType}:{this.HolidayDestinationValue}";

        /// <summary>
        /// The holiday dates, earliest in the year first. The year is not part of what is matched
        /// (D64), so they sort by month and day.
        /// </summary>
        public List<TimeConditionRule> HolidayRules() =>
            this.Rules
                .Where(r => r.IsHoliday)
                .OrderBy(r => r.HolidayOn()?.Month ?? 0)
                .ThenBy(r => r.HolidayOn()?.Day ?? 0)
                .ThenBy(r => r.SortOrder)
                .ToList();

        /// <summary>The two open columns as the one string the picker posts (D35).</summary>
        public string OpenDestinationKey() =>
            this.OpenDestinationValue.Length == 0
                ? this.OpenDestinationType
                : $"{this.OpenDestinationType}:{this.OpenDestinationValue}";

        /// <summary>
        /// Where a call outside the open hours goes. Never null: a pair that does not read back
        /// becomes Hangup, because a call has to end somewhere and nowhere is worse than here.
        /// </summary>
        public Destination ToClosedDestination() =>
            Destination.TryParse(this.ClosedDestinationKey(), out var destination) ? destination : Destination.Hangup;

        /// <summary>The condition as a destination, for the catalog and the dialplan (D63).</summary>
        public Destination ToDestination() => new(Models.DestinationType.TimeCondition, this.PlayExtension);

        /// <summary>Where a call on a holiday goes, unless that date says otherwise.</summary>
        public Destination ToHolidayDestination() =>
            Destination.TryParse(this.HolidayDestinationKey(), out var destination) ? destination : Destination.Hangup;

        /// <summary>Where a call inside the open hours goes.</summary>
        public Destination ToOpenDestination() =>
            Destination.TryParse(this.OpenDestinationKey(), out var destination) ? destination : Destination.Hangup;

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(Name))
                errors.Add("Name is required.");
            else if (Name.Length > 64)
                errors.Add("Name must be 64 characters or fewer.");
            else if (!NamePattern().IsMatch(Name))
                errors.Add("Name may only contain letters, digits, spaces and . , ' - _ ( ) &");

            if (Description.Length > 128)
                errors.Add("Description must be 128 characters or fewer.");
            else if (Description.Length > 0 && !DescriptionPattern().IsMatch(Description))
                errors.Add("Description may only contain letters, digits, spaces and . , ' - _ ( ) &");

            // The same rule as an extension number, because it is dialled the same way and shares
            // the same context. Feature codes all start with *, so digits cannot collide with one;
            // collisions with a real extension, a ring group, an announcement or an IVR are checked
            // by the repository, which can see them.
            if (PlayExtension.Length > 0 && !Extension.IsValidNumber(PlayExtension))
                errors.Add("Play extension must be 2 to 6 digits, or blank for none.");

            foreach (var rule in Rules)
                errors.AddRange(rule.Validate());

            // Two rules on the same date would write the same GotoIfTime twice, and the second
            // could never be reached. Month and day are all the dialplan matches (D64), so
            // "25 December 2026" and "25 December 2027" are the same rule written twice.
            var dates = HolidayRules()
                .Select(r => r.HolidayOn())
                .Where(d => d != null)
                .Select(d => (d!.Value.Month, d!.Value.Day))
                .ToList();

            if (dates.Count != dates.Distinct().Count())
                errors.Add("Two holidays fall on the same day of the year. A holiday repeats every year, so the year is not part of the date.");

            ValidateDestination(errors, OpenDestinationKey(), "open hours");
            ValidateDestination(errors, ClosedDestinationKey(), "closed hours");
            ValidateDestination(errors, HolidayDestinationKey(), "holidays");

            foreach (var rule in Rules.Where(r => r.IsHoliday && r.HasOverride))
                ValidateDestination(errors, rule.DestinationKey(), $"the holiday on {rule.HolidayDate}");

            return errors;
        }

        /// <summary>
        /// The open windows, in the order the condition lists them. Order changes nothing — any one
        /// of them matching means open — but it is the order an admin wrote them in, so it is the
        /// order they are written out in.
        /// </summary>
        public List<TimeConditionRule> WeeklyRules() =>
            this.Rules
                .Where(r => r.IsWeekly)
                .OrderBy(r => r.SortOrder)
                .ThenBy(r => r.StartTime, StringComparer.Ordinal)
                .ToList();

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex DescriptionPattern();

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex NamePattern();

        /// <summary>
        /// One destination of this condition: it has to read back, and it must not be this very
        /// condition. A condition may point at <i>another</i> one — that is how "closed" hands over
        /// to a second set of hours — but pointing at its own play extension is a call that never
        /// leaves the context (D63).
        /// </summary>
        private void ValidateDestination(List<string> errors, string key, string what)
        {
            if (!Destination.TryParse(key, out var destination))
            {
                errors.Add($"Choose where a call in {what} should go.");
                return;
            }

            if (destination.Type == Models.DestinationType.TimeCondition &&
                PlayExtension.Length > 0 &&
                string.Equals(destination.Value, PlayExtension, StringComparison.Ordinal))
            {
                errors.Add($"A time condition cannot send a call in {what} back to itself.");
            }
        }
    }
}
