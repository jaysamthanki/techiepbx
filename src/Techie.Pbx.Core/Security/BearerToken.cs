using System.Security.Cryptography;
using System.Text;

namespace Techie.Pbx.Core.Security
{
    /// <summary>
    /// The <c>Authorization: Bearer &lt;token&gt;</c> header, as the voicemail notify endpoint uses
    /// it (D129). A script running as the asterisk user has no Entra cookie and no browser to get
    /// one with, so the one credential it can carry is a shared secret out of the settings — the
    /// same shape of trust path <see cref="BasicAuth"/> is for a desk phone.
    ///
    /// Pure functions over strings so the gate can be tested without a web server.
    /// </summary>
    public static class BearerToken
    {
        /// <summary>The scheme name, as it appears in the header.</summary>
        public const string Scheme = "Bearer";

        /// <summary>
        /// Whether this header carries exactly this token. A blank expected token is never matched
        /// by anything: an endpoint whose only protection is a secret that was never set is not an
        /// open endpoint, it is a closed one.
        /// </summary>
        public static bool Matches(string? header, string expected)
        {
            if (expected.Length == 0)
                return false;

            if (!TryParse(header, out var token))
                return false;

            // Compared byte by byte without stopping at the first difference, so how much of a
            // guess was right cannot be read off how long the answer took.
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(token),
                Encoding.UTF8.GetBytes(expected));
        }

        /// <summary>
        /// Splits an "Authorization: Bearer &lt;token&gt;" header. False for anything that is not
        /// that: another scheme, nothing at all, or a value long enough to be an attack rather
        /// than a token.
        /// </summary>
        public static bool TryParse(string? header, out string token)
        {
            token = "";

            var text = (header ?? "").Trim();

            if (text.Length is 0 or > 1024)
                return false;

            if (!text.StartsWith(Scheme + " ", StringComparison.OrdinalIgnoreCase))
                return false;

            token = text[(Scheme.Length + 1)..].Trim();

            return token.Length > 0;
        }
    }
}
