using System.Globalization;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Reports;

namespace Techie.Pbx.Web.Pages.Reports
{
    /// <summary>
    /// One line of the call list: a stored record as an admin reads it, times in the site's zone
    /// and the direction and the parties worked out from the trunks and extensions as they are now
    /// (<see cref="CdrParties"/>).
    /// </summary>
    public class CallRow
    {
        public long CdrID { get; set; }

        public CallDirection Direction { get; set; }

        /// <summary>Talk and ring time together, as hh:mm:ss so the column sorts as text.</summary>
        public string Duration { get; set; } = "";

        /// <summary>The extension on the call, when it was internal: the one dialled, or else the caller.</summary>
        public string? Extension { get; set; }

        /// <summary>The extension the call came from, with its name, or the caller's number.</summary>
        public string From { get; set; } = "";

        /// <summary>The number the call used on the trunk: the DID it came in on, or the caller ID it went out as.</summary>
        public string? Line { get; set; }

        /// <summary>When the call started, local, "yyyy-MM-dd HH:mm:ss" so the column sorts as text.</summary>
        public string Started { get; set; } = "";

        public CallStatus Status { get; set; }

        /// <summary>The extension the call went to, with its name, or the number dialled.</summary>
        public string To { get; set; } = "";

        /// <summary>The trunk the call went over, or null for an internal one.</summary>
        public string? Trunk { get; set; }

        public static CallRow For(Cdr cdr, PbxEndpoints endpoints, IReadOnlyDictionary<string, Cdr> origins, TimeZoneInfo zone)
        {
            var parties = CdrParties.For(cdr, endpoints, origins);

            return new CallRow
            {
                CdrID = cdr.CdrID,
                Direction = parties.Direction,
                Duration = Seconds(cdr.DurationSeconds),
                Extension = parties.Direction == CallDirection.Internal
                    ? CdrChannels.Endpoint(cdr.DestinationChannel) ?? CdrChannels.Endpoint(cdr.Channel)
                    : null,
                From = parties.From,
                Line = parties.Line,
                Started = Local(cdr.StartUtc, zone) ?? cdr.StartUtc,
                Status = CallStatuses.For(cdr.Disposition),
                To = parties.To,
                Trunk = parties.Trunk,
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
