using Techie.Pbx.Core.Reports;

namespace Techie.Pbx.Web.Pages.Reports
{
    /// <summary>
    /// The filters as the page's form sends them, in the query string so a filtered report is a
    /// URL that can be bookmarked or shared. Dates are days in the site's own zone; turning them
    /// into the UTC range the repository wants is <see cref="ToFilter"/>'s job.
    /// </summary>
    public class ReportFilterForm
    {
        /// <summary>"inbound", "outbound", "internal", or empty for any.</summary>
        public string? Direction { get; set; }

        /// <summary>An extension number, or empty for any.</summary>
        public string? Extension { get; set; }

        /// <summary>The first day included, in the site's zone.</summary>
        public DateOnly? From { get; set; }

        /// <summary>"answered", "missed", "busy", "failed", or empty for any.</summary>
        public string? Status { get; set; }

        /// <summary>The last day included, in the site's zone.</summary>
        public DateOnly? To { get; set; }

        /// <summary>Fills in the days nobody chose, by <see cref="ReportRange.Default"/>: today when neither was.</summary>
        public void Default(DateOnly today) =>
            (this.From, this.To) = ReportRange.Default(this.From, this.To, today);

        /// <summary>
        /// The repository's filter: midnight at the start of <see cref="From"/> to midnight at the
        /// end of <see cref="To"/>, both in the site's zone and turned into UTC. Returns null and
        /// the reasons when the form does not make sense.
        /// </summary>
        public CdrFilter? ToFilter(TimeZoneInfo zone, DateOnly today, out List<string> errors)
        {
            errors = new List<string>();
            this.Default(today);

            CallDirection? direction = null;
            if (!string.IsNullOrEmpty(this.Direction))
            {
                direction = Parse<CallDirection>(this.Direction);
                if (direction == null)
                    errors.Add("Direction must be inbound, outbound or internal.");
            }

            CallStatus? status = null;
            if (!string.IsNullOrEmpty(this.Status))
            {
                status = Parse<CallStatus>(this.Status);
                if (status == null)
                    errors.Add("Status must be answered, missed, busy or failed.");
            }

            if (this.To < this.From)
                errors.Add("The \"to\" day has to be on or after the \"from\" day.");

            var extension = string.IsNullOrWhiteSpace(this.Extension) ? null : this.Extension.Trim();
            if (extension != null && !CdrFilter.IsValidExtension(extension))
                errors.Add("Pick an extension from the list.");

            if (errors.Count > 0)
                return null;

            return new CdrFilter
            {
                Direction = direction,
                Extension = extension,
                FromUtc = Utc(this.From!.Value, zone),
                Status = status,
                ToUtc = Utc(this.To!.Value.AddDays(1), zone),
            };
        }

        /// <summary>
        /// One of the enum's names, any case. Not Enum.TryParse on its own, which also takes "7"
        /// and any other number as a value nobody defined.
        /// </summary>
        private static T? Parse<T>(string value) where T : struct, Enum =>
            Enum.GetValues<T>().Cast<T?>().FirstOrDefault(v => string.Equals(v.ToString(), value, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Midnight at the start of a local day, in UTC. A day that starts inside a daylight-saving
        /// gap starts at the first minute that exists.
        /// </summary>
        private static DateTime Utc(DateOnly day, TimeZoneInfo zone)
        {
            var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

            while (zone.IsInvalidTime(local))
                local = local.AddMinutes(1);

            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }
    }
}
