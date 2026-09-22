using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Reports
{
    /// <summary>
    /// Totals per extension and per trunk (F5), counted over the same records the report lists, so
    /// the two can never disagree. Counted here rather than in SQL because who a record belongs to
    /// comes out of its channel names, and <see cref="CdrParties"/> is the one place that reads
    /// those. An extension line is only ever an extension's channel: a caller from outside is a
    /// number, not a line of the totals.
    ///
    /// What is counted is records, not calls: a ring-all to three phones is three records, one
    /// answered and two missed, and each phone's line says so.
    /// </summary>
    public static class CdrTotals
    {
        /// <summary>Every extension that was on a record, by number, then every trunk, by name.</summary>
        public static (List<CdrTotal> Extensions, List<CdrTotal> Trunks) For(
            IEnumerable<Cdr> cdrs, PbxEndpoints endpoints, IReadOnlyDictionary<string, Cdr> origins)
        {
            var extensions = new Dictionary<string, CdrTotal>(StringComparer.Ordinal);
            var trunks = new Dictionary<string, CdrTotal>(StringComparer.Ordinal);

            foreach (var cdr in cdrs)
            {
                var answered = CallStatuses.For(cdr.Disposition) == CallStatus.Answered;
                var parties = CdrParties.For(cdr, endpoints, origins);
                var caller = CdrChannels.Endpoint(cdr.Channel);
                var called = CdrChannels.Endpoint(cdr.DestinationChannel);

                // The extension that made the call, even when this record's caller is the Local
                // channel of a forward: an outbound call counts for it and for the trunk.
                if (parties.FromExtension != null)
                    Count(extensions, parties.FromExtension, answered, receiving: false);

                // A call from an extension to itself is one record, and one line of the totals.
                if (parties.ToExtension != null && !string.Equals(parties.ToExtension, parties.FromExtension, StringComparison.Ordinal))
                    Count(extensions, parties.ToExtension, answered, receiving: true);

                if (endpoints.IsTrunk(caller))
                    Count(trunks, caller!, answered, receiving: true);

                if (endpoints.IsTrunk(called) && !string.Equals(called, caller, StringComparison.Ordinal))
                    Count(trunks, called!, answered, receiving: false);
            }

            return (Sorted(extensions, numeric: true), Sorted(trunks, numeric: false));
        }

        /// <summary>
        /// Adds one record to a line. "Receiving" is whether the call came to this side: an
        /// extension that was dialled, or a trunk a call arrived on. Only those can miss a call.
        /// </summary>
        private static void Count(Dictionary<string, CdrTotal> totals, string name, bool answered, bool receiving)
        {
            if (!totals.TryGetValue(name, out var total))
            {
                total = new CdrTotal { Name = name };
                totals[name] = total;
            }

            total.Total++;

            if (answered)
                total.Answered++;
            else if (receiving)
                total.Missed++;
        }

        /// <summary>Extensions in numeric order, so 99 comes before 100; trunks by name.</summary>
        private static List<CdrTotal> Sorted(Dictionary<string, CdrTotal> totals, bool numeric) => totals.Values
            .OrderBy(t => numeric ? t.Name.Length : 0)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }
}
