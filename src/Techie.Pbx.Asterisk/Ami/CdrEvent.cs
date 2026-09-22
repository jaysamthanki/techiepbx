using System.Globalization;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// Reads a Cdr event into a <see cref="Cdr"/> (F5). The header names are cdr_manager's in
    /// Asterisk 22 — Source, Destination, DestinationContext, BillableSeconds — plus the LinkedID and
    /// Sequence that cdr_manager.conf maps onto the event. Each field also answers to the name the
    /// CDR engine itself uses for it (Src, Dst, DContext, BillSec and so on), so a mapping or a
    /// version that spells them that way still lands in the right column.
    ///
    /// cdr_manager writes times as "2026-09-22 14:03:05" in the server's own zone, with no zone in
    /// the text. The server clock is meant to be UTC (D74), but the zone is taken rather than
    /// assumed, and the caller says which one it is.
    /// </summary>
    public static class CdrEvent
    {
        /// <summary>The event name cdr_manager sends each record as.</summary>
        public const string EventName = "Cdr";

        private static readonly string[] TimeFormats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFF" };

        /// <summary>Whether a message is a call record, and so something to store.</summary>
        public static bool Is(AmiMessage message) =>
            string.Equals(message.EventName, EventName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The record an event carries. Throws <see cref="FormatException"/> when there is no
        /// UniqueID, which is the one thing a record cannot be stored without. A start time that
        /// cannot be read falls back to the end time and then to <paramref name="receivedUtc"/>,
        /// because a record filed at roughly the right time is worth more than no record.
        /// </summary>
        public static Cdr ToCdr(AmiMessage message, TimeZoneInfo serverZone, DateTime receivedUtc)
        {
            var uniqueID = Text(message, "UniqueID");
            if (uniqueID == null)
                throw new FormatException("The Cdr event has no UniqueID.");

            var answer = Time(message, serverZone, "AnswerTime", "Answer");
            var end = Time(message, serverZone, "EndTime", "End");
            var start = Time(message, serverZone, "StartTime", "Start") ?? end ?? CdrRepository.Format(receivedUtc);

            return new Cdr
            {
                AccountCode = Text(message, "AccountCode"),
                AmaFlags = Text(message, "AMAFlags"),
                AnswerUtc = answer,
                BillSecSeconds = Number(message, "BillableSeconds", "BillSec"),
                CallerID = Text(message, "CallerID", "Clid"),
                Channel = Text(message, "Channel") ?? "",
                Dcontext = Text(message, "DestinationContext", "DContext") ?? "",
                DestinationChannel = Text(message, "DestinationChannel", "DstChannel"),
                Disposition = Text(message, "Disposition") ?? "",
                Dst = Text(message, "Destination", "Dst") ?? "",
                DurationSeconds = Number(message, "Duration"),
                EndUtc = end,
                LastApplication = Text(message, "LastApplication", "LastApp"),
                LastData = Text(message, "LastData"),
                LinkedID = Text(message, "LinkedID"),
                Sequence = long.TryParse(Text(message, "Sequence"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence)
                    ? sequence
                    : null,
                Src = Text(message, "Source", "Src") ?? "",
                StartUtc = start,
                UniqueID = uniqueID,
            };
        }

        private static int? Number(AmiMessage message, params string[] names) =>
            int.TryParse(Text(message, names), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

        /// <summary>The first of the names that has a value; blank counts as none.</summary>
        private static string? Text(AmiMessage message, params string[] names)
        {
            foreach (var name in names)
            {
                var value = message.Get(name);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }

        /// <summary>
        /// A time as stored: the server's local text turned into UTC. Null for an empty field — an
        /// unanswered call has no answer time — and for one that does not parse.
        /// </summary>
        private static string? Time(AmiMessage message, TimeZoneInfo serverZone, params string[] names)
        {
            var text = Text(message, names);
            if (text == null)
                return null;

            if (!DateTime.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                return null;

            // A clock that skips an hour for daylight saving has local times that never happened.
            // A UTC server has none, and a record claiming one is not worth guessing about.
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (serverZone.IsInvalidTime(unspecified))
                return null;

            return CdrRepository.Format(TimeZoneInfo.ConvertTimeToUtc(unspecified, serverZone));
        }
    }
}
