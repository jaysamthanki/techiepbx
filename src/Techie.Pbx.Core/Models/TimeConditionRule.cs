using System.Globalization;
using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// One line of a time condition: either "open 09:00-17:00, Monday to Friday" or "closed on
    /// 25 December" (D63). Both live in one table because they are edited in one form and read
    /// together; which fields mean anything depends on <see cref="Kind"/>.
    ///
    /// The fields are shaped by what <c>GotoIfTime</c> can actually match — a time range, a set of
    /// weekdays, a day of the month and a month. There is no year field in the dialplan, so a
    /// holiday date recurs annually and the year is only ever a way of writing the date down (D64).
    /// </summary>
    public partial class TimeConditionRule
    {
        /// <summary>Every weekday set, which is what <c>GotoIfTime</c> writes as "*".</summary>
        public const int AllDays = 0b111_1111;

        /// <summary>Monday is the first bit, so 0b000_0001. Sunday is the last, 0b100_0000.</summary>
        public const int Monday = 1 << 0;

        public const int Tuesday = 1 << 1;

        public const int Wednesday = 1 << 2;

        public const int Thursday = 1 << 3;

        public const int Friday = 1 << 4;

        public const int Saturday = 1 << 5;

        public const int Sunday = 1 << 6;

        /// <summary>Asterisk's own day names, in the order the bits are numbered.</summary>
        private static readonly string[] DayNames = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };

        /// <summary>And its month names, which are what the month field of a time spec takes.</summary>
        private static readonly string[] MonthNames =
            { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

        /// <summary>A bit per weekday, Monday = 1. Nothing to do with a holiday rule.</summary>
        public int DaysMask { get; set; }

        /// <summary>
        /// A holiday rule's own destination, by name, or empty to use the condition's holiday
        /// destination. Nothing to do with a weekly rule, which always means "open".
        /// </summary>
        public string DestinationType { get; set; } = "";

        /// <summary>What that destination points at. Empty for Hangup and for "no override".</summary>
        public string DestinationValue { get; set; } = "";

        /// <summary>The end of the open window, "HH:MM". Nothing to do with a holiday rule.</summary>
        public string EndTime { get; set; } = "";

        /// <summary>
        /// The date this rule closes the site on, "YYYY-MM-DD". Nothing to do with a weekly rule,
        /// and the year is not matched: see <see cref="MonthField"/> (D64).
        /// </summary>
        public string HolidayDate { get; set; } = "";

        public TimeConditionRuleKind Kind { get; set; }

        /// <summary>The start of the open window, "HH:MM". Nothing to do with a holiday rule.</summary>
        public string StartTime { get; set; } = "";

        /// <summary>Where the rule sits in its condition's list, which is the order it is written in.</summary>
        public int SortOrder { get; set; }

        public long TimeConditionID { get; set; }

        public long TimeConditionRuleID { get; set; }

        /// <summary>Whether this holiday rule sends the call somewhere of its own (D63).</summary>
        public bool HasOverride => this.DestinationType.Length > 0;

        public bool IsHoliday => this.Kind == TimeConditionRuleKind.Holiday;

        public bool IsWeekly => this.Kind == TimeConditionRuleKind.Weekly;

        /// <summary>
        /// The day-of-month field of a <c>GotoIfTime</c> spec, e.g. "25". Empty when the date does
        /// not read back, which validation refuses before anything renders.
        /// </summary>
        public string DayOfMonthField() =>
            this.HolidayOn() is { } date ? date.Day.ToString(CultureInfo.InvariantCulture) : "";

        /// <summary>
        /// The weekday field of a <c>GotoIfTime</c> spec: "*" for every day, "mon-fri" for a run,
        /// "mon&amp;wed&amp;fri" for a scattering, and both together where that is what was chosen.
        /// </summary>
        public string DaysField()
        {
            if (this.DaysMask == AllDays)
                return "*";

            var runs = new List<string>();
            var day = 0;

            while (day < DayNames.Length)
            {
                if ((this.DaysMask & (1 << day)) == 0)
                {
                    day++;
                    continue;
                }

                var start = day;
                while (day + 1 < DayNames.Length && (this.DaysMask & (1 << (day + 1))) != 0)
                    day++;

                runs.Add(start == day ? DayNames[start] : $"{DayNames[start]}-{DayNames[day]}");
                day++;
            }

            return string.Join("&", runs);
        }

        /// <summary>The two destination columns as the one string the picker posts (D35).</summary>
        public string DestinationKey() =>
            this.DestinationValue.Length == 0 ? this.DestinationType : $"{this.DestinationType}:{this.DestinationValue}";

        /// <summary>The date this rule falls on, or null when it is not a date at all.</summary>
        public DateOnly? HolidayOn() =>
            DateOnly.TryParseExact(this.HolidayDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;

        /// <summary>Whether a string is the "HH:MM" of a 24-hour clock.</summary>
        public static bool IsValidTime(string time) => TimePattern().IsMatch(time);

        /// <summary>
        /// The month field of a <c>GotoIfTime</c> spec, e.g. "dec". With the day of the month it is
        /// the whole of what the dialplan can match: <b>the year is dropped</b>, because
        /// <c>GotoIfTime</c> has nowhere to put one, so "25 December 2026" matches every
        /// 25 December (D64).
        /// </summary>
        public string MonthField() =>
            this.HolidayOn() is { } date ? MonthNames[date.Month - 1] : "";

        /// <summary>The time field of a <c>GotoIfTime</c> spec, e.g. "09:00-17:00".</summary>
        public string TimeRange() => $"{this.StartTime}-{this.EndTime}";

        /// <summary>
        /// Where a holiday rule sends the call, or null when it has no override and the condition's
        /// own holiday destination is what should be used.
        /// </summary>
        public Destination? ToDestination() =>
            this.HasOverride && Destination.TryParse(this.DestinationKey(), out var destination) ? destination : null;

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (this.IsWeekly)
            {
                if (this.DaysMask is <= 0 or > AllDays)
                    errors.Add("An open-hours rule needs at least one day of the week.");

                if (!IsValidTime(this.StartTime) || !IsValidTime(this.EndTime))
                    errors.Add("Open-hours times must be HH:MM on a 24-hour clock, e.g. 09:00 and 17:30.");
                else if (string.CompareOrdinal(this.StartTime, this.EndTime) >= 0)
                    errors.Add($"Open hours {this.StartTime}-{this.EndTime} end before they start.");

                // A weekly rule is what "open" means, so it has no destination of its own: the
                // condition's open destination is where every one of them leads.
                if (this.HasOverride)
                    errors.Add("An open-hours rule has no destination of its own; the condition's open destination is used.");
            }
            else
            {
                if (this.HolidayOn() == null)
                    errors.Add("A holiday needs a date, as YYYY-MM-DD.");

                if (this.HasOverride && !Destination.TryParse(this.DestinationKey(), out _))
                    errors.Add("Choose where that holiday should send a call, or leave it on the condition's holiday destination.");
            }

            return errors;
        }

        [GeneratedRegex(@"^([01][0-9]|2[0-3]):[0-5][0-9]\z")]
        private static partial Regex TimePattern();
    }
}
