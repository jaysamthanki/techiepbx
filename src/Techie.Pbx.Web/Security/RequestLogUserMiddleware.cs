using System.Security.Claims;
using Techie.Pbx.Core.Security;
using Techie.Pbx.Web.Controllers;

namespace Techie.Pbx.Web.Security
{
    /// <summary>
    /// Puts the username a provisioning request carried into <see cref="HttpContext.User"/>, so the
    /// W3C request log has something to write in its <c>cs-username</c> column for a desk phone
    /// (D116). An admin's requests already have a name there, because the Entra cookie puts one on
    /// every request it authenticates; a phone's do not, because a phone's Basic credentials are
    /// checked inside the provisioning controllers rather than by an authentication handler.
    ///
    /// What it deliberately does not do:
    ///
    /// <list type="bullet">
    /// <item><b>Authenticate anything.</b> The identity is built with no authentication type, so
    /// <c>IsAuthenticated</c> stays false and this can never stand in for signing in. It is a label
    /// on a log line, not a credential — the password is not even looked at, let alone checked, and
    /// <see cref="PolycomController"/> and <see cref="YealinkController"/> still do the real check
    /// themselves against the stored settings.</item>
    /// <item><b>Touch anything but provisioning.</b> Only the two phone endpoints, both of which are
    /// <c>[AllowAnonymous]</c> and neither of which reads <c>User</c>, so nothing but the log ever
    /// sees what this sets.</item>
    /// </list>
    ///
    /// Added to the pipeline after <c>UseAuthentication</c>, because the authentication middleware
    /// replaces <c>HttpContext.User</c> whenever a scheme hands it a principal, and only added at
    /// all when the request log is on — switched off it is absent, not inert.
    /// </summary>
    public class RequestLogUserMiddleware
    {
        /// <summary>
        /// Longer than a provisioning username may be anyway (SettingsValidation caps it at 64), so
        /// a header full of junk cannot make a log line full of junk.
        /// </summary>
        private const int MaxUsernameLength = 64;

        private readonly RequestDelegate next;

        public RequestLogUserMiddleware(RequestDelegate next)
        {
            this.next = next;
        }

        public Task InvokeAsync(HttpContext context)
        {
            // A real sign-in always wins, the same rule LocalBypassMiddleware follows: a request
            // that already has an authenticated identity keeps the name that identity gave it.
            if (context.User.Identity?.IsAuthenticated != true && Username(context) is { } username)
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, username) }));

            return this.next(context);
        }

        /// <summary>The two paths a phone fetches its configuration from, and nothing else.</summary>
        private static bool IsProvisioning(PathString path) =>
            path.StartsWithSegments(PolycomController.RoutePrefix) ||
            path.StartsWithSegments(YealinkController.RoutePrefix);

        /// <summary>
        /// The username half of a Basic header on a provisioning request, or null when there is
        /// nothing worth naming. Whitespace and control characters are refused rather than escaped:
        /// a W3C log is one record per line with space separated fields, and a value that could
        /// split either is not a username we ever issued.
        /// </summary>
        private static string? Username(HttpContext context)
        {
            if (!IsProvisioning(context.Request.Path))
                return null;

            if (!BasicAuth.TryParse(context.Request.Headers.Authorization, out var username, out _))
                return null;

            var text = username.Trim();

            if (text.Length is 0 or > MaxUsernameLength)
                return null;

            return text.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) ? null : text;
        }
    }
}
