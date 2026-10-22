using log4net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Web.Security;

namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// The Teams tab's sign-in (D183). The webphone page inside the Teams client asks Teams for a
    /// token for this app's API (TeamsJS getAuthToken, granted silently to the signed-in user) and
    /// posts it here; <see cref="TeamsSso"/> validates it against Entra and this controller signs
    /// the user in with the same session cookie the browser flow issues, so the reload that
    /// follows lands on the real softphone.
    ///
    /// Anonymous on purpose: the caller is by definition not signed in yet. Everything the token
    /// proves is checked in <see cref="TeamsSso.ValidateAsync"/> — a refused token never reaches
    /// a sign-in. The local bypass middleware (D24) does not apply: it fills in only for requests
    /// it can see, and this endpoint runs in the same pipeline it already filtered.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/teamsauth")]
    public class TeamsAuthController : ControllerBase
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(TeamsAuthController));

        private readonly SettingsRepository settings;
        private readonly IConfiguration configuration;

        public TeamsAuthController(IConfiguration configuration)
        {
            this.settings = new SettingsRepository(PbxDatabase.Current);
            this.configuration = configuration;
        }

        /// <summary>
        /// Exchanges a Teams token for a session. 204 on success — the page reloads into a signed-
        /// in phone. A refused token is 401 with one sentence, so the tab can say what went wrong
        /// instead of a bare failure.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Authenticate([FromBody] TeamsAuthRequest request, CancellationToken cancellationToken)
        {
            var stored = this.settings.GetAll();

            var clientId = stored.GetValueOrDefault(SettingsKeys.TeamsAppClientId)
                ?? this.configuration["AzureAd:ClientId"];
            var tenantId = stored.GetValueOrDefault(SettingsKeys.TeamsAppTenantId)
                ?? this.configuration["AzureAd:TenantId"];
            var siteHost = stored.GetValueOrDefault(SettingsKeys.TeamsAppSiteUrl)
                ?? this.Request.Host.Host;

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(tenantId))
            {
                Log.Warn("Teams SSO refused: no Entra registration configured (appsettings or TeamsApp.* settings).");
                return this.StatusCode(503, new { message = "The server has no Entra registration configured, so Teams sign-in cannot work." });
            }

            var token = request?.Token?.Trim() ?? "";

            if (token.Length == 0)
            {
                return this.BadRequest(new { message = "No token was sent." });
            }

            var principal = await TeamsSso.For(tenantId, clientId, siteHost).ValidateAsync(token, cancellationToken);

            if (principal == null)
            {
                // The reason is in the log; the caller gets the same refusal whatever failed.
                return this.Unauthorized(new { message = "Teams could not sign you in: the token was refused. Sign in through the browser once instead." });
            }

            Log.Info($"Teams SSO signed in {principal.Identity?.Name} (client {clientId}, tenant {tenantId})");

            await this.HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            return this.NoContent();
        }
    }

    /// <summary>What the tab's script posts: the token Teams handed it.</summary>
    public class TeamsAuthRequest
    {
        public string? Token { get; set; }
    }
}
