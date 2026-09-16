using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// "Numbers that look like this go out over that trunk." Routes are tried in priority order,
    /// and a number that matches none of them does not go out at all (D45).
    /// </summary>
    public partial class OutboundRoute
    {
        /// <summary>Highest priority number allowed; 1 is tried first.</summary>
        public const int MaxPriority = 999;

        /// <summary>The Asterisk pattern, including its leading underscore, e.g. "_1NXXXXXXXXX".</summary>
        public string DialPattern { get; set; } = "";

        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = "";
        public long OutboundRouteID { get; set; }

        /// <summary>Lower is tried first. Ties are broken by name, so the order is never random.</summary>
        public int Priority { get; set; } = 100;

        public long TrunkID { get; set; }

        /// <summary>
        /// The dialplan context this route's pattern lives in. One per route, because Asterisk
        /// searches included contexts in the order they are included, which is how "first match
        /// wins" is made to mean our order rather than Asterisk's idea of the most specific
        /// pattern (D46).
        /// </summary>
        public string Context => $"outbound-{this.Name}";

        /// <summary>
        /// Why this pattern may not be used, or null when it may. International dialling is the
        /// bill a compromised PBX runs up, so a route cannot be written that reaches it (D47).
        /// </summary>
        public static string? InternationalReason(string pattern)
        {
            var body = pattern.StartsWith('_') ? pattern[1..] : pattern;
            if (body.Length == 0)
                return null;

            // 011 (North America) and 00 (most of the rest) are the international prefixes, so a
            // pattern that starts with a 0 at all is refused rather than guessed about.
            if (body[0] == '0')
                return "An outbound route may not start with 0: 00 and 011 are international dialling.";

            // A wildcard that can match 0 matches those prefixes too, which is the classic
            // "one route to everywhere" hole.
            if (body[0] is 'X' or '.' or 'x')
                return "An outbound route may not start with a wildcard that can match 0, because it would match international numbers as well.";

            if (body[0] == '[')
            {
                var close = body.IndexOf(']');
                var set = close > 0 ? body[1..close] : "";

                if (set.Contains('0') || set.Contains("0-"))
                    return "An outbound route may not start with a character set that includes 0, because it would match international numbers as well.";
            }

            return null;
        }

        /// <summary>
        /// Whether this is a pattern Asterisk would understand: the classes we allow are digits,
        /// X, N, Z, a [...] set, and a trailing dot (D46). Returns null when it is fine.
        /// </summary>
        public static string? PatternSyntaxError(string pattern)
        {
            if (!pattern.StartsWith('_'))
                return "A dial pattern starts with an underscore, e.g. _1NXXXXXXXXX.";

            var body = pattern[1..];
            if (body.Length is 0 or > 40)
                return "A dial pattern must be 1 to 40 characters after the underscore.";

            for (var i = 0; i < body.Length; i++)
            {
                var c = body[i];

                if (char.IsAsciiDigit(c) || c is 'X' or 'N' or 'Z')
                    continue;

                if (c == '.')
                {
                    if (i != body.Length - 1)
                        return "A dot matches anything that follows, so it can only be the last character of a pattern.";

                    continue;
                }

                if (c == '[')
                {
                    var close = body.IndexOf(']', i);
                    if (close < 0)
                        return "A [ in a dial pattern needs a matching ].";

                    if (!CharacterSetPattern().IsMatch(body[(i + 1)..close]))
                        return "A [...] set may only contain digits and ranges, e.g. [24-6].";

                    i = close;
                    continue;
                }

                return $"'{c}' is not allowed in a dial pattern. Use digits, X, N, Z, [...] or a trailing dot.";
            }

            return null;
        }

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (!NamePattern().IsMatch(Name))
                errors.Add("Name must start with a letter and contain only letters, digits and dashes (up to 32 characters).");

            var syntax = PatternSyntaxError(DialPattern);
            if (syntax != null)
                errors.Add(syntax);
            else
            {
                var international = InternationalReason(DialPattern);
                if (international != null)
                    errors.Add(international);
            }

            if (TrunkID <= 0)
                errors.Add("A route needs a trunk to send calls out over.");

            if (Priority is < 1 or > MaxPriority)
                errors.Add($"Priority must be between 1 and {MaxPriority}.");

            return errors;
        }

        [GeneratedRegex(@"^([0-9](-[0-9])?)+$")]
        private static partial Regex CharacterSetPattern();

        [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9\-]{0,31}$")]
        private static partial Regex NamePattern();
    }
}
