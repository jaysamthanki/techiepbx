using System.Security.Cryptography;
using System.Text;

namespace Techie.Pbx.Core.Security
{
    /// <summary>
    /// HTTP Basic authentication, as the provisioning endpoint uses it (D77). A desk phone has no
    /// cookie jar and cannot sign in with Entra ID, so the one credential it can carry is the
    /// user:pass embedded in the DHCP option 160 URL, and that arrives as an Authorization header.
    ///
    /// Pure functions over strings so the gate can be tested without a web server.
    /// </summary>
    public static class BasicAuth
    {
        /// <summary>The scheme name, as it appears in the header and in the challenge.</summary>
        public const string Scheme = "Basic";

        /// <summary>
        /// Whether this header carries exactly these credentials. Blank expected values are never
        /// matched by anything: provisioning with no username or password configured is off, not
        /// open (D77).
        /// </summary>
        public static bool Matches(string? header, string expectedUsername, string expectedPassword)
        {
            if (expectedUsername.Length == 0 || expectedPassword.Length == 0)
                return false;

            if (!TryParse(header, out var username, out var password))
                return false;

            // Compared byte by byte without stopping at the first difference, so how much of a
            // guess was right cannot be read off how long the answer took.
            return FixedTimeEquals(username, expectedUsername) & FixedTimeEquals(password, expectedPassword);
        }

        /// <summary>
        /// Splits an "Authorization: Basic base64(user:pass)" header. False for anything that is
        /// not that: another scheme, base64 that does not decode, or no colon in the result.
        /// </summary>
        public static bool TryParse(string? header, out string username, out string password)
        {
            username = "";
            password = "";

            var text = (header ?? "").Trim();

            // Long enough to be a real credential and short enough not to be a denial of service.
            if (text.Length is 0 or > 1024)
                return false;

            if (!text.StartsWith(Scheme + " ", StringComparison.OrdinalIgnoreCase))
                return false;

            var encoded = text[(Scheme.Length + 1)..].Trim();

            var decoded = new byte[encoded.Length];
            if (!Convert.TryFromBase64String(encoded, decoded, out var written))
                return false;

            var pair = Encoding.UTF8.GetString(decoded, 0, written);

            // A password may contain a colon; a username may not, so the first one splits it.
            var separator = pair.IndexOf(':');
            if (separator < 0)
                return false;

            username = pair[..separator];
            password = pair[(separator + 1)..];

            return true;
        }

        private static bool FixedTimeEquals(string actual, string expected) =>
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(actual),
                Encoding.UTF8.GetBytes(expected));
    }
}
