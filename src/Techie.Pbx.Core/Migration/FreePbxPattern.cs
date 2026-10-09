namespace Techie.Pbx.Core.Migration
{
    /// <summary>
    /// FreePBX's outbound dial-pattern grammar, translated into the pattern an
    /// <see cref="Models.OutboundRoute"/> stores (D170: translation lives in the importer, so the
    /// manifest stays a faithful copy and grammar fixes happen in one tested place).
    ///
    /// The two grammars are nearly one: both are Asterisk's. FreePBX writes them without the
    /// leading underscore, and accepts a few things ours does not:
    ///
    /// <list type="bullet">
    /// <item><c>N</c> (2-9), <c>X</c> (0-9), <c>Z</c> (1-9), digits and <c>[...]</c> sets carry over
    /// as they are; lower-case <c>n x z</c> become upper-case, which is what Asterisk reads them as.</item>
    /// <item>A trailing <c>.</c> carries over. Anywhere else it is refused, as the route form refuses it.</item>
    /// <item>A <c>-</c> outside a set is dropped: Asterisk ignores it (<c>NXX-XXXX</c> is <c>NXXXXXX</c>).</item>
    /// <item>The legacy <c>prefix|pattern</c> form becomes the whole number plus a strip of the
    /// prefix's length, which is what FreePBX did with it. Only an all-digit prefix is taken.</item>
    /// <item><c>!</c>, <c>*</c>, <c>#</c>, <c>+</c> and anything else are refused with a reason.</item>
    /// </list>
    ///
    /// The result is then held to every rule a hand-typed route is, international included (D47):
    /// a FreePBX <c>011.</c> route is refused here exactly as it would be on the form.
    /// </summary>
    public static class FreePbxPattern
    {
        /// <summary>
        /// Translates one FreePBX pattern. True with the stored pattern (underscore and all) and
        /// how many digits to strip; false with the reason it cannot be a TNPBX route.
        /// </summary>
        public static bool TryTranslate(string? freePbxPattern, out string pattern, out int stripDigits, out string problem)
        {
            pattern = "";
            stripDigits = 0;
            problem = "";

            var text = (freePbxPattern ?? "").Trim();
            if (text.StartsWith('_'))
                text = text[1..];

            var bar = text.IndexOf('|');
            if (bar >= 0)
            {
                var prefix = text[..bar];

                if (prefix.Length == 0 || !prefix.All(char.IsAsciiDigit) || text.IndexOf('|', bar + 1) >= 0)
                {
                    problem = "Its prefix|pattern form has a prefix that is not plain digits, which has no TNPBX equivalent.";
                    return false;
                }

                stripDigits = prefix.Length;
                text = prefix + text[(bar + 1)..];
            }

            if (text.Length == 0)
            {
                problem = "The pattern is empty.";
                return false;
            }

            var body = new System.Text.StringBuilder();
            var inSet = false;

            foreach (var c in text)
            {
                if (inSet)
                {
                    body.Append(c);
                    inSet = c != ']';
                    continue;
                }

                switch (c)
                {
                    case >= '0' and <= '9':
                    case '.':
                        body.Append(c);
                        break;

                    case 'N' or 'n' or 'X' or 'x' or 'Z' or 'z':
                        body.Append(char.ToUpperInvariant(c));
                        break;

                    case '[':
                        body.Append(c);
                        inSet = true;
                        break;

                    // Asterisk ignores dashes in a pattern, so FreePBX lets people type them.
                    case '-':
                        break;

                    case '!':
                        problem = "'!' (match zero or more, end immediately) has no TNPBX equivalent. Use a trailing '.' instead.";
                        return false;

                    default:
                        problem = $"'{c}' is not something a TNPBX route pattern can match. Routes match digits, N, X, Z, [...] and a trailing dot.";
                        return false;
                }
            }

            var candidate = "_" + body;

            var error = Models.OutboundRoute.PatternSyntaxError(candidate) ?? Models.OutboundRoute.InternationalReason(candidate);
            if (error != null)
            {
                problem = error;
                stripDigits = 0;
                return false;
            }

            pattern = candidate;
            return true;
        }
    }
}
