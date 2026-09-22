using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// "Numbers that look like this go out over that trunk." Routes are tried in priority order,
    /// and a number that matches none of them does not go out at all (D45).
    /// </summary>
    public partial class OutboundRoute
    {
        /// <summary>
        /// The digit nothing this system dials out may start with: 011 (North America) and 00
        /// (most of the rest) are the international prefixes, so a leading 0 is refused rather than
        /// guessed about (D47). A route's pattern, a route's prepend digits (D109) and an
        /// extension's forwarding list (D130) all make the same refusal, so they all name it here.
        /// </summary>
        public const char InternationalPrefix = '0';

        /// <summary>Highest priority number allowed; 1 is tried first.</summary>
        public const int MaxPriority = 999;

        /// <summary>
        /// What a call that matched this route calls out as, or empty for none — FreePBX's "option
        /// CID" (D125). Either form <see cref="CallerIDFormat"/> reads: a bare number, or
        /// <c>"Acme Sales" &lt;17141234567&gt;</c>.
        ///
        /// It is a fallback, not an override: the dialplan writes it guarded, so an extension that
        /// claimed a caller ID of its own keeps it and this fills in for everyone else. Empty leaves
        /// the trunk's own <c>callerid</c> to say who we are, exactly as before this existed.
        /// </summary>
        public string CallerID { get; set; } = "";

        /// <summary>
        /// The Asterisk pattern, including its leading underscore, e.g. "_1NXXXXXXXXX". The
        /// underscore is what tells Asterisk this is a pattern rather than a literal number, so
        /// <see cref="NormalizePattern"/> adds it to anything that arrives without one.
        /// </summary>
        public string DialPattern { get; set; } = "";

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// The music on hold class a caller who went out over this route hears whenever the far side
        /// holds them, or null for none (D125). The outbound twin of
        /// <see cref="InboundRoute.MohClassID"/>: a reference rather than a copy of the name, so
        /// renaming a class renames it here at the next apply and deleting one puts this back to null.
        /// </summary>
        public long? MohClassID { get; set; }

        public string Name { get; set; } = "";

        /// <summary>
        /// Digits written in front of the number before it is sent to the trunk (D109). A site in
        /// the 714 area code routes <c>_NXXXXXX</c> with a prepend of <c>1714</c>, so a caller
        /// dials seven digits and the provider sees eleven. Empty means none.
        /// </summary>
        public string PrependDigits { get; set; } = "";

        public long OutboundRouteID { get; set; }

        /// <summary>Lower is tried first. Ties are broken by name, so the order is never random.</summary>
        public int Priority { get; set; } = 100;

        /// <summary>
        /// How many leading dialled digits to drop before the rest is sent to the trunk (D109).
        /// Zero means none. "Dial 9 for an outside line" is a pattern like <c>_9NXXXXXXXXX</c>
        /// with a strip of 1.
        /// </summary>
        public int StripDigits { get; set; }

        public long TrunkID { get; set; }

        /// <summary>
        /// What a caller dialled, as the number the trunk is given: the prepend in front of the
        /// dialled digits minus the stripped ones. Rendered as <c>${EXTEN}</c>-style expressions
        /// in the dialplan; this is the string a human reads in the table and tests.
        /// </summary>
        public string SentNumberExpression =>
            this.PrependDigits + (this.StripDigits > 0 ? $"${{EXTEN:{this.StripDigits}}}" : "${EXTEN}");

        /// <summary>
        /// The dialplan context this route's pattern lives in. One per route, because Asterisk
        /// searches included contexts in the order they are included, which is how "first match
        /// wins" is made to mean our order rather than Asterisk's idea of the most specific
        /// pattern (D46).
        /// </summary>
        public string Context => $"outbound-{this.Name}";

        /// <summary>
        /// Patterns start with an underscore, and a caller typing <c>NXXXXXX</c> into the form
        /// means the pattern, not the literal number. Rather than fail them for it, the
        /// repository stores what they meant. Returns the pattern with the underscore on it.
        /// </summary>
        public static string NormalizePattern(string pattern)
        {
            var trimmed = pattern.Trim();
            return trimmed.StartsWith('_') || trimmed.Length == 0 ? trimmed : "_" + trimmed;
        }

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
            if (body[0] == InternationalPrefix)
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

            // A prepend is chosen digits, not a match, so it cannot smuggle in a wildcard — but
            // 00 and 011 are still the international prefixes, and a prepend starting with 0 is
            // the D47 guard walked around from the other side (D109).
            if (PrependDigits.Length > 0)
            {
                if (PrependDigits.Length > 10 || !PrependDigits.All(char.IsAsciiDigit))
                    errors.Add("Prepend digits must be 1 to 10 digits, or empty.");
                else if (PrependDigits[0] == InternationalPrefix)
                    errors.Add("Prepend digits may not start with 0: 00 and 011 are international dialling.");
            }

            // The caller ID this route presents, in either of the two forms an admin might type
            // (D125). It ends up inside a Set(CALLERID(all)=...) in the dialplan, so what is allowed
            // is what can be written there and nothing else.
            var callerID = CallerIDFormat.Error(CallerID, "Caller ID");
            if (callerID != null)
                errors.Add(callerID);

            if (StripDigits is < 0 or > 10)
                errors.Add("Strip digits must be between 0 and 10.");

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
