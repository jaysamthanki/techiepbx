using System.Security.Claims;

namespace Techie.Pbx.Web.Security
{
    /// <summary>
    /// Who the footer says is signed in. Entra issues two names and they are not the same thing:
    /// <c>preferred_username</c>, which is the UPN and what Microsoft.Identity.Web makes
    /// <see cref="System.Security.Principal.IIdentity.Name"/>, and <c>name</c>, which is the display
    /// name a person recognises as theirs. The display name is preferred and the UPN is the
    /// fallback, so the footer reads "Jay Thanki" where the token carries one and
    /// "jay@example.com" where it does not.
    ///
    /// Nothing has to be mapped at sign-in for this to work: the OpenID Connect handler puts every
    /// id token claim on the identity already. Both spellings of the claim are looked for, because
    /// which one arrives depends on whether inbound claim mapping is on — Microsoft.Identity.Web
    /// turns it off, so the short JWT name is the usual one, but a handler configured the other way
    /// would deliver the WS-Fed URI instead.
    ///
    /// The local sign-in bypass (D24) has only a <see cref="ClaimTypes.Name"/> of its own, so it
    /// falls through to the fallback and names itself — which is the honest answer for a session
    /// that never signed in.
    /// </summary>
    public static class SignedInUser
    {
        /// <summary>The claim Entra puts the display name in, as the JWT spells it.</summary>
        public const string DisplayNameClaim = "name";

        /// <summary>The same claim after inbound claim mapping, for a handler that leaves it on.</summary>
        public const string MappedDisplayNameClaim = ClaimTypes.Name;

        /// <summary>
        /// What to call this user, or an empty string when nobody is signed in. Never throws and
        /// never returns null: it is rendered into every page's footer, and a layout is the worst
        /// possible place for an exception.
        /// </summary>
        public static string DisplayName(ClaimsPrincipal? user)
        {
            if (user?.Identity?.IsAuthenticated != true)
                return "";

            // The mapped spelling is only worth anything when it is not just the UPN again, which
            // is what Microsoft.Identity.Web points the identity's own name claim at.
            var display = Text(user.FindFirst(DisplayNameClaim)?.Value);
            var name = Text(user.Identity.Name);

            if (display.Length == 0)
            {
                var mapped = Text(user.FindFirst(MappedDisplayNameClaim)?.Value);

                if (!string.Equals(mapped, name, StringComparison.Ordinal))
                    display = mapped;
            }

            return display.Length > 0 ? display : name;
        }

        private static string Text(string? value) => (value ?? "").Trim();
    }
}
