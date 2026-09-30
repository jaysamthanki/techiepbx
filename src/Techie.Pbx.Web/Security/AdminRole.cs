using System.Security.Claims;

namespace Techie.Pbx.Web.Security
{
    /// <summary>
    /// What makes a signed-in user an admin (D165): the Entra app role, not the sign-in itself.
    /// Any tenant user may sign in — that is what lets a phone user reach /phone (D164) — and
    /// only a user Entra has assigned the app role to gets the admin pages and the API.
    ///
    /// Both spellings of the claim are looked for, as <see cref="SignedInUser"/> does for the
    /// display name: the id token calls it <c>roles</c>, and a handler with inbound claim mapping
    /// on delivers the same values as <see cref="ClaimTypes.Role"/> instead.
    ///
    /// The local sign-in bypass (D24) carries the role too: break-glass has to be an admin.
    /// </summary>
    public static class AdminRole
    {
        /// <summary>The claim Entra puts app roles in, as the JWT spells it.</summary>
        public const string ClaimType = "roles";

        /// <summary>The same claim after inbound claim mapping, for a handler that leaves it on.</summary>
        public const string MappedClaimType = ClaimTypes.Role;

        /// <summary>The authorization policy every admin page and API controller sits behind.</summary>
        public const string PolicyName = "AdminOnly";

        /// <summary>
        /// The app role's value, as the Entra app registration declares it. The app role UI's
        /// Display name may carry spaces ("TNPBX Admin") but its Value may not, so the value is
        /// this and only this — Entra emits exactly it in the token.
        /// </summary>
        public const string Value = "TNPBX.Admin";

        /// <summary>
        /// Whether this user is signed in and carries the admin role, under either spelling of
        /// the claim, compared without regard to case. Never throws: the layout calls it on
        /// every page.
        /// </summary>
        public static bool IsAdmin(ClaimsPrincipal? user)
        {
            if (user?.Identity?.IsAuthenticated != true)
                return false;

            return user.Claims.Any(claim =>
                (claim.Type == ClaimType || claim.Type == MappedClaimType) &&
                string.Equals((claim.Value ?? "").Trim(), Value, StringComparison.OrdinalIgnoreCase));
        }
    }
}
