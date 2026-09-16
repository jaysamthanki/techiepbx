using System.Security.Claims;
using log4net;

namespace Techie.Pbx.Web.Security
{
    /// <summary>
    /// Gives requests from the allowed networks an identity of their own, after the Entra cookie
    /// has had its say and before authorization runs. Pages, htmx partials and API controllers all
    /// go through here, so all three work the same way (D24).
    ///
    /// Only added to the pipeline when the bypass is enabled: switched off it is not merely inert,
    /// it is absent.
    /// </summary>
    public class LocalBypassMiddleware
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(LocalBypassMiddleware));

        private readonly RequestDelegate next;
        private readonly LocalBypassSettings settings;

        public LocalBypassMiddleware(RequestDelegate next, LocalBypassSettings settings)
        {
            this.next = next;
            this.settings = settings;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var address = context.Connection.RemoteIpAddress;

            // A real Entra sign-in always wins; the bypass only fills in for the absence of one.
            if (context.User.Identity?.IsAuthenticated != true && this.settings.Allows(address))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Name, LocalBypassSettings.IdentityName) },
                    LocalBypassSettings.AuthenticationType));

                Log.Info($"Local sign-in bypass: {context.Request.Method} {context.Request.Path} from {address} treated as '{LocalBypassSettings.IdentityName}'");
            }

            await this.next(context);
        }
    }
}
