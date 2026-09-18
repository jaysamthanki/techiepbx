namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// The zones a server may be told its open hours are written in: the ids of the machine's own
    /// zone database, which on Linux are IANA names ("America/Los_Angeles"). Built once and shared
    /// by the two screens that offer the choice — the time conditions page and the settings page —
    /// and by the rule that decides whether a stored value is allowed, so a zone that can be chosen
    /// is exactly a zone that can be stored and written into a GotoIfTime (D74, D75).
    ///
    /// The list is the system's rather than one of ours to keep current: tzdata changes every time
    /// a country moves its clocks, and Asterisk reads the same database we are reading here.
    /// </summary>
    public static class SystemTimezones
    {
        /// <summary>
        /// What a TNPBX server's clock is meant to be set to (D74), and so both the first entry in
        /// the list and what the code falls back to when nobody has chosen a zone.
        /// </summary>
        public const string Utc = "Etc/UTC";

        /// <summary>
        /// Every zone this machine knows, <see cref="Utc"/> first and the rest in name order. A
        /// machine with no zone database at all answers with just <see cref="Utc"/>, which is the
        /// honest list for a server that cannot name any other zone.
        /// </summary>
        public static IReadOnlyList<string> All { get; } = Build();

        /// <summary>
        /// Whether this machine really has a zone database, i.e. whether <see cref="All"/> is a
        /// list to check a name against or only the fallback. Validation stops insisting a zone be
        /// in the list when it is not, so a machine without tzdata cannot reject every zone an
        /// admin picks, including the right one.
        /// </summary>
        public static bool IsListed => All.Count > 1;

        /// <summary>Whether this is one of the zones this server can actually name.</summary>
        public static bool IsKnown(string? value) =>
            value != null && All.Contains(value, StringComparer.Ordinal);

        /// <summary>
        /// The shape of an IANA zone name: letters, digits and the few punctuation marks one is
        /// made of. It is what keeps a stray character out of a conf file, and it is why every
        /// entry of <see cref="All"/> is safe to write into one.
        /// </summary>
        public static bool IsZoneName(string value) =>
            value.Length is > 0 and <= 64 &&
            value.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '+');

        private static List<string> Build()
        {
            var zones = new List<string>();

            try
            {
                zones.AddRange(TimeZoneInfo.GetSystemTimeZones()
                    .Select(zone => zone.Id)
                    .Where(IsZoneName));
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // No readable zone database. The fallback below is then the whole list.
            }

            return zones
                .Where(id => !string.Equals(id, Utc, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .Prepend(Utc)
                .ToList();
        }
    }
}
