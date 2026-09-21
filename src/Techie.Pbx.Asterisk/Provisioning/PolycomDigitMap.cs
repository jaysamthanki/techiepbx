using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// What a Polycom phone may dial without the user pressing Send, and — the part that matters —
    /// what it must <b>not</b> send early (D124).
    ///
    /// A Polycom digit map is a list of patterns separated by <c>|</c>. The phone dials the moment
    /// the digits it has fully match a pattern, unless that pattern ends in <c>T</c>, in which case
    /// it waits <c>dialplan.digitmap.timeOut</c> seconds for another digit. Nothing else stops it:
    /// a pattern that matches at four digits sends at four digits even though a longer pattern is
    /// still partially matched. That is how the old fixed map, whose first pattern was <c>xxxx</c>,
    /// cut an attended transfer to a ten-digit mobile down to its first four digits on a real Poly
    /// Edge 450.
    ///
    /// So the rule here, and it is the whole of the design: <b>a pattern may be eager only when no
    /// dialable number is longer than it and starts with it.</b> Everything else ends in
    /// <c>T</c> — the user gets the digits they typed after the timeout, or at once when they press
    /// <c>#</c>, which Polycom treats as "send now".
    ///
    /// <list type="bullet">
    /// <item><b>Eager:</b> the ten-digit number, the eleven-digit <c>1</c> + number, and the N11
    /// services. N11 is safe because the NANP reserves those codes: no area code is N11, so
    /// <c>211</c> cannot be the start of a ten-digit number.</item>
    /// <item><b>Timed:</b> the extensions (every extension here is shorter than ten digits, so
    /// every one of them could be the start of a real number), seven-digit local dialing, which is
    /// the first seven digits of a ten-digit number, and the feature codes, whose length varies —
    /// <c>*8</c> takes an extension after it.</item>
    /// </list>
    ///
    /// The extension patterns are built from the extensions this system actually has, one per
    /// length, with the first digit narrowed to the digits really in use: a site on 100–104 gets
    /// <c>1xxT</c>, not <c>xxxT</c>. Disabled extensions count too — what is being described is
    /// which lengths and leading digits mean "this is an extension", and a phone must not need a
    /// re-poll to dial an extension that was switched back on this morning.
    ///
    /// A pure function of the extension list, which is what lets a golden test pin it.
    /// </summary>
    public static class PolycomDigitMap
    {
        /// <summary>Polycom's wildcard for one digit.</summary>
        private const char AnyDigit = 'x';

        /// <summary>
        /// A feature code: a star, two digits, then anything, ended by the timeout. <c>*97</c> and
        /// <c>*43</c> are three digits; <c>*8</c> plus the extension that is ringing is a directed
        /// pickup and can be any length, which is why this cannot be eager.
        /// </summary>
        private const string FeatureCode = "*xx.T";

        /// <summary>
        /// Seven-digit local dialing, which the outbound routes allow. It is the first seven digits
        /// of a ten-digit number, so it waits rather than cutting one short.
        /// </summary>
        private const string Local = "[2-9]xxxxxxT";

        /// <summary>The ten-digit number: as long as a NANP number gets without a leading 1.</summary>
        private const string National = "[2-9]xxxxxxxxx";

        /// <summary>Eleven digits, the leading 1 and the number. Nothing dialable is longer.</summary>
        private const string NationalWithOne = "1xxxxxxxxxx";

        /// <summary>
        /// The operator. Timed, because a 0 on its own is also how an international call starts to
        /// look, and one digit is no evidence of anything.
        /// </summary>
        private const string Operator = "0T";

        /// <summary>
        /// 911 and its siblings. Eager, and safely so: the NANP reserves N11 as service codes, so
        /// no area code looks like one and no ten-digit number starts with one.
        /// </summary>
        private const string Services = "[2-9]11";

        /// <summary>Polycom's "wait for the inter-digit timeout before sending".</summary>
        private const char Timed = 'T';

        /// <summary>
        /// The map for a system with these extensions, in the order it is written: the extensions
        /// first because they are what is dialled all day, then the codes, then the outside world.
        /// Order is presentation only — the phone matches every pattern, and eagerness rather than
        /// position is what decides when it sends.
        /// </summary>
        public static string For(IEnumerable<Extension> extensions)
        {
            var numbers = extensions
                .Select(e => e.Number)
                .Where(Extension.IsValidNumber)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var patterns = numbers
                .Select(n => n.Length)
                .Distinct()
                .OrderBy(length => length)
                .Select(length => ExtensionPattern(numbers, length))
                .ToList();

            patterns.Add(FeatureCode);
            patterns.Add(Services);
            patterns.Add(Local);
            patterns.Add(National);
            patterns.Add(NationalWithOne);
            patterns.Add(Operator);

            return string.Join('|', patterns);
        }

        /// <summary>
        /// One length of extension: the digits that really start one of them, then a wildcard per
        /// remaining digit, then the timeout. Always timed — every extension is two to six digits
        /// and so could be the start of a ten- or eleven-digit number.
        /// </summary>
        private static string ExtensionPattern(List<string> numbers, int length)
        {
            var leading = numbers
                .Where(n => n.Length == length)
                .Select(n => n[0])
                .Distinct()
                .OrderBy(digit => digit)
                .ToList();

            return $"{LeadingDigits(leading)}{new string(AnyDigit, length - 1)}{Timed}";
        }

        /// <summary>
        /// The first digit as a pattern: the digit itself when a site's extensions all start the
        /// same way, a list in brackets when they do not, and the plain wildcard when every digit
        /// is in use — which is the same thing said shorter.
        /// </summary>
        private static string LeadingDigits(List<char> digits) => digits.Count switch
        {
            1 => digits[0].ToString(),
            10 => AnyDigit.ToString(),
            _ => $"[{new string(digits.ToArray())}]",
        };
    }
}
