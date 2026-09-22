using System.Text;

namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// What a value from outside is allowed to look like by the time it reaches a message. The
    /// same idea as <c>ConfText.Safe</c> for conf files: a caller ID name is a string a stranger
    /// chose, and it ends up in a subject line — which is a header, where a line break is not a
    /// formatting problem but an injected header.
    ///
    /// Pure functions over strings, so the rule can be tested without a mail server.
    /// </summary>
    public static class MailText
    {
        /// <summary>
        /// A block of text that may have lines in it — a transcript. Line breaks survive; every
        /// other control character does not, and the whole thing is capped so that a speech
        /// engine having a very bad day cannot post a megabyte into an email.
        /// </summary>
        public static string Block(string? value, int maxLength)
        {
            var sb = new StringBuilder();

            foreach (var character in (value ?? "").ReplaceLineEndings("\n"))
            {
                var next = character == '\n' ? '\n' : char.IsControl(character) ? ' ' : character;

                sb.Append(next);

                if (sb.Length == maxLength)
                    break;
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// One line of ordinary text: no control characters, no line breaks, no runs of spaces,
        /// and never longer than <paramref name="maxLength"/>. Anything dropped is dropped
        /// silently — this is a caller ID, not a form somebody is filling in, and there is nobody
        /// to tell.
        /// </summary>
        public static string Plain(string? value, int maxLength)
        {
            var sb = new StringBuilder();

            foreach (var character in value ?? "")
            {
                // A line break, a tab and a null are all the same thing here: not text.
                var next = char.IsControl(character) ? ' ' : character;

                if (next == ' ' && (sb.Length == 0 || sb[^1] == ' '))
                    continue;

                sb.Append(next);

                if (sb.Length == maxLength)
                    break;
            }

            return sb.ToString().TrimEnd();
        }
    }
}
