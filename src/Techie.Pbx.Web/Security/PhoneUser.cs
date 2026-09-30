using System.Security.Claims;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Security
{
    /// <summary>
    /// Which extensions a signed-in user may open in the web client (D164, D165). An admin may
    /// open any enabled, web-enabled extension. Anyone else may open only the ones whose
    /// voicemail email is the address they signed in with — and an extension with no voicemail
    /// email belongs to nobody that way (no fallback, D165).
    ///
    /// Pure functions of the principal and the extensions handed in, so the page's dropdown and
    /// the handler that gives out the SIP secret cannot disagree about who owns what.
    /// </summary>
    public static class PhoneUser
    {
        /// <summary>The claim Entra puts the mail address in, as the JWT spells it.</summary>
        public const string EmailClaim = "email";

        /// <summary>The same claim after inbound claim mapping, for a handler that leaves it on.</summary>
        public const string MappedEmailClaim = ClaimTypes.Email;

        /// <summary>The UPN, as the JWT spells it: the fallback for a token with no email claim.</summary>
        public const string UpnClaim = "preferred_username";

        /// <summary>The UPN after inbound claim mapping.</summary>
        public const string MappedUpnClaim = ClaimTypes.Upn;

        /// <summary>
        /// The address this user signed in with, trimmed: the email claim where the token has
        /// one, the UPN where it does not, and an empty string when there is neither or nobody
        /// is signed in. Never null.
        /// </summary>
        public static string Email(ClaimsPrincipal? user)
        {
            if (user?.Identity?.IsAuthenticated != true)
                return "";

            foreach (var type in new[] { EmailClaim, MappedEmailClaim, UpnClaim, MappedUpnClaim })
            {
                var value = Text(user.FindFirst(type)?.Value);

                if (value.Length > 0)
                    return value;
            }

            return "";
        }

        /// <summary>
        /// The extensions this user may open in the web client, out of the ones handed in: only
        /// enabled, web-enabled extensions, all of them for an admin and the user's own for
        /// anyone else. Empty when nobody is signed in.
        /// </summary>
        public static List<Extension> Extensions(ClaimsPrincipal? user, IEnumerable<Extension> extensions)
        {
            if (user?.Identity?.IsAuthenticated != true)
                return new List<Extension>();

            var web = extensions.Where(e => e.Enabled && e.WebClient);

            if (AdminRole.IsAdmin(user))
                return web.ToList();

            var email = Email(user);

            return web.Where(e => Owns(email, e)).ToList();
        }

        /// <summary>
        /// Whether this sign-in address is the extension's voicemail email, ignoring case and
        /// surrounding space. An empty address owns nothing, so a user with no email claim never
        /// matches an extension with no voicemail email.
        /// </summary>
        public static bool Owns(string? email, Extension extension)
        {
            var address = Text(email);

            return address.Length > 0 &&
                   string.Equals(address, Text(extension.VoicemailEmail), StringComparison.OrdinalIgnoreCase);
        }

        private static string Text(string? value) => (value ?? "").Trim();
    }
}
