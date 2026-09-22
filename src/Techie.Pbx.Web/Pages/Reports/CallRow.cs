using System.Globalization;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Reports;

namespace Techie.Pbx.Web.Pages.Reports
{
    /// <summary>
    /// One line of the call list: a stored record as an admin reads it, times in the site's zone
    /// and the direction worked out from the trunks as they are now.
    /// </summary>
    public class CallRow
    {
        public long CdrID { get; set; }

        public CallDirection Direction { get; set; }

        /// <summary>Talk and ring time together, as hh:mm:ss so the column sorts as text.</summary>
        public string Duration { get; set; } = "";

        /// <summary>The extension on the call, when it was internal: the one dialled, or else the caller.</summary>
        public string? Extension { get; set; }

        public string From { get; set; } = "";

        /// <summary>When the call started, local, "yyyy-MM-dd HH:mm:ss" so the column sorts as text.</summary>
        public string Started { get; set; } = "";

        public CallStatus Status { get; set; }

        public string To { get; set; } = "";

        /// <summary>The trunk the call went over, or null for an internal one.</summary>
        public string? Trunk { get; set; }

        public static CallRow For(Cdr cdr, IReadOnlySet<string> trunkNames, TimeZoneInfo zone)
        {
            var direction = CdrChannels.Direction(cdr, trunkNames);

            return new CallRow
            {
                CdrID = cdr.CdrID,
                Direction = direction,
                Duration = Seconds(cdr.DurationSeconds),
                Extension = direction == CallDirection.Internal
                    ? CdrChannels.Endpoint(cdr.DestinationChannel) ?? CdrChannels.Endpoint(cdr.Channel)
                    : null,
                From = cdr.Src,
                Started = Local(cdr.StartUtc, zone) ?? cdr.StartUtc,
                Status = CallStatuses.For(cdr.Disposition),
                To = cdr.Dst,
                Trunk = CdrChannels.Trunk(cdr, trunkNames),
            };
        }

        /// <summary>A stored UTC time in the site's zone, or null when there is none.</summary>
        public static string? Local(string? utc, TimeZoneInfo zone)
        {
            if (!DateTime.TryParseExact(utc, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                return null;

            return TimeZoneInfo.ConvertTimeFromUtc(parsed, zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>hh:mm:ss, or empty when Asterisk did not say.</summary>
        public static string Seconds(int? seconds) =>
            seconds is { } value ? TimeSpan.FromSeconds(value).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : "";
    }
}
