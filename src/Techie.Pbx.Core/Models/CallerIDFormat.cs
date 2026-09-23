using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// The two forms a caller ID may be typed in, and the one place that reads them (D125):
    /// <c>"Acme Sales" &lt;17141234567&gt;</c>, or a bare number. FreePBX's own two forms, because an
    /// admin who has set this up before will type one of them and should not be told off for it.
    ///
    /// Shared because two things store a caller ID — an extension's own (<see
    /// cref="Extension.OutboundCallerID"/>) and an outbound route's (<see
    /// cref="OutboundRoute.CallerID"/>) — and both end up in the same place, Asterisk's
    /// <c>CALLERID(all)</c>, so two ideas of what is allowed would be one too many.
    ///
    /// The rules are deliberately narrower than Asterisk's. A number is digits only: no <c>+</c>, no
    /// spaces and no punctuation, like every other number this system stores (a DID, a prepend). A
    /// name may not contain brackets, commas, colons or quotes, because what is generated is
    /// <c>Set(CALLERID(all)="Name" &lt;number&gt;)</c> inside an <c>ExecIf</c> — Asterisk reads an
    /// application's arguments to the matching bracket and reads a colon as the start of
    /// <c>ExecIf</c>'s else branch, and the quotes around the name are ours to add rather than the
    /// admin's to type.
    /// </summary>
    public static partial class CallerIDFormat
    {
        public const int MaxNameLength = 32;
        public const int MaxNumberLength = 15;

        /// <summary>
        /// Why this caller ID may not be used, or null when it may. Empty is always fine: it means
        /// "name nothing here", which is what the whole precedence chain is built on.
        /// </summary>
        /// <param name="label">
        /// What to call the field in the message, e.g. "Caller ID" or "Outbound caller ID", so the
        /// admin is told which box on the form is the problem.
        /// </param>
        public static string? Error(string value, string label)
        {
            var trimmed = value.Trim();

            if (trimmed.Length == 0)
                return null;

            if (!TryParse(trimmed, out _, out _))
                return $"{label} must be a number of up to {MaxNumberLength} digits, or a name and " +
                       $"number in the form \"Acme Sales\" <17141234567>. A name may be up to " +
                       $"{MaxNameLength} letters, digits, spaces and . ' - _ & — no brackets, " +
                       "commas, colons or quotes of its own.";

            return null;
        }

        /// <summary>
        /// The two parts of a caller ID, or false when this is not one. The name is empty for the
        /// bare-number form and for <c>&lt;17141234567&gt;</c> typed with nothing in front of it.
        /// </summary>
        public static bool TryParse(string value, out string name, out string number)
        {
            name = "";
            number = "";

            var trimmed = value.Trim();
            if (trimmed.Length == 0)
                return false;

            var open = trimmed.IndexOf('<');
            if (open < 0)
            {
                if (!NumberPattern().IsMatch(trimmed))
                    return false;

                number = trimmed;
                return true;
            }

            if (!trimmed.EndsWith('>'))
                return false;

            var digits = trimmed[(open + 1)..^1].Trim();
            if (!NumberPattern().IsMatch(digits))
                return false;

            // The quotes are optional on the way in and added on the way out, so "Acme" <100> and
            // Acme <100> are the same caller ID as far as this system is concerned.
            var named = trimmed[..open].Trim().Trim('"').Trim();
            if (named.Length > 0 && !NamePattern().IsMatch(named))
                return false;

            name = named;
            number = digits;
            return true;
        }

        [GeneratedRegex(@"^[\p{L}\p{N} .'\-_&]{1,32}\z")]
        private static partial Regex NamePattern();

        [GeneratedRegex(@"^[0-9]{1,15}\z")]
        private static partial Regex NumberPattern();
    }
}
