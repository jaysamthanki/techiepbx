using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Techie.Pbx.Web.Security
{
    /// <summary>
    /// Turns a Teams SSO token into this app's signed-in principal (D183).
    ///
    /// Inside the Teams client the webphone cannot run the normal Entra redirect: Microsoft's
    /// login pages refuse to render in an iframe, so the tab gets a blank screen. Instead the
    /// page's script asks Teams for a token for this app's own API (TeamsJS getAuthToken, which
    /// the signed-in Teams user grants silently) and posts it to
    /// <see cref="Controllers.TeamsAuthController"/>, which validates it here and signs the user
    /// in with the same session cookie the browser flow issues.
    ///
    /// The token is an Entra access token issued for the app registration's Application ID URI
    /// (api://sitehost/clientId). Validation checks issuer and signing keys against the tenant's
    /// own OpenID Connect metadata, audience against the API ID URI or the client id, the tid
    /// against the configured tenant, and the exposed scope against <see cref="Scope"/>.
    /// </summary>
    public class TeamsSso
    {
        /// <summary>
        /// The scope the app registration must expose for the Teams token to carry — the admin
        /// creates it in the Developer Portal / Entra (the D183 checklist says exactly what to
        /// add). A token without it is refused, whatever else it validates.
        /// </summary>
        public const string Scope = "access_as_user";

        /// <summary>
        /// The spelling the tenant's existing consent carries (D194). The first live tenant
        /// consented its scope under the name "TeamsApp" before the documented access_as_user
        /// naming was applied, and a v1 token mints the consent record's stored text — renaming
        /// the scope on the registration does not change what already-consented tokens say, and
        /// the stale grant is invisible in the portal because it belongs to the Teams client
        /// app, not to ours. Accepting the alias is honoring that admin's own consent: the token
        /// is still Entra-signed, tenant-checked, audience-checked and a user token, and in any
        /// tenant without a consent under this name Entra refuses the exchange outright, so this
        /// adds no way in. A tenant that grants the documented name gets that one instead.
        /// </summary>
        public const string AliasScope = "TeamsApp";

        private static readonly object CacheLock = new();
        private static readonly Dictionary<(string TenantId, string ClientId, string Host), TeamsSso> Cache = new();

        private readonly ConfigurationManager<OpenIdConnectConfiguration> configurationManager;
        private readonly string tenantId;
        private readonly string clientId;
        private readonly string siteHost;

        private TeamsSso(string tenantId, string clientId, string siteHost)
        {
            this.tenantId = tenantId;
            this.clientId = clientId;
            this.siteHost = siteHost;

            this.configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                $"https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(new HttpClient()));
        }

        /// <summary>
        /// A validator for one tenant/client/host triple, reusing the OpenID Connect metadata
        /// across requests: a fresh HttpClient and metadata fetch per POST would put a round trip
        /// to login.microsoftonline.com on every sign-in. The triples in play are a handful at
        /// most, so a plain dictionary keyed on the whole triple is enough.
        /// </summary>
        public static TeamsSso For(string tenantId, string clientId, string siteHost)
        {
            lock (CacheLock)
            {
                var key = (tenantId, clientId, siteHost);

                if (!Cache.TryGetValue(key, out var sso))
                {
                    Cache[key] = sso = new TeamsSso(tenantId, clientId, siteHost);
                }

                return sso;
            }
        }

        /// <summary>The Application ID URI the token's audience must match, plus the bare client id.</summary>
        public static string[] Audiences(string clientId, string siteHost) =>
            new[] { $"api://{siteHost}/{clientId}", clientId };

        /// <summary>
        /// Both issuers a tenant's tokens can carry. TeamsJS getAuthToken hands out v1-endpoint
        /// tokens (sts.windows.net), not the v2 ones the browser OIDC flow sees — the first live
        /// exchange was refused on exactly that difference (D189) — so both spellings are valid
        /// and the tid claim below is what ties a token to this tenant either way.
        /// </summary>
        public static string[] IssuersOf(string tenantId) =>
        [
            $"https://sts.windows.net/{tenantId}/",
            $"https://login.microsoftonline.com/{tenantId}/v2.0",
        ];

        /// <summary>
        /// The principal the token stands for, or null when it does not stand for one. Never
        /// throws: a failed validation is a sign-in refusal, and its reason goes in the log.
        /// </summary>
        public async Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken cancellationToken)
        {
            OpenIdConnectConfiguration configuration;

            try
            {
                configuration = await this.configurationManager.GetConfigurationAsync(cancellationToken);
            }
            catch (Exception error)
            {
                Log.Warn($"Teams SSO: could not read the tenant's OpenID Connect metadata: {error.Message}");
                return null;
            }

            var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidIssuers = IssuersOf(this.tenantId),
                ValidAudiences = Audiences(this.clientId, this.siteHost),
                IssuerSigningKeys = configuration.SigningKeys,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
            });

            if (!result.IsValid || result.ClaimsIdentity == null)
            {
                Log.Warn($"Teams SSO: token refused: {result.Exception?.Message ?? "no reason given"}");
                return null;
            }

            // The audience and signature only say the token is Entra's for this API; the tenant
            // claim is what says the user belongs to the tenant this box serves.
            string? tid = null;

            if (result.Claims.TryGetValue("tid", out var tidValue))
            {
                tid = tidValue as string;
            }

            if (!string.Equals(tid, this.tenantId, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn($"Teams SSO: token refused: tenant {tid ?? "(none)"} is not ours.");
                return null;
            }

            // An application token (client credentials) has roles but no scp and no user behind
            // it; only a user's delegated token may sign a person in.
            var scopes = "";

            if (result.Claims.TryGetValue("scp", out var scpValue))
            {
                scopes = scpValue as string ?? "";
            }

            if (!scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(Scope, StringComparer.OrdinalIgnoreCase) &&
                !scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(AliasScope, StringComparer.OrdinalIgnoreCase))
            {
                // aud and azp come along (D193): a token minted for the bare client id rather than
                // the api:// URI, or one the Teams client minted for itself, reads differently
                // here, and scp alone cannot tell them apart.
                string? Audited(string claim) =>
                    result.Claims.TryGetValue(claim, out var v) ? v?.ToString() : null;
                Log.Warn($"Teams SSO: token refused: does not carry the {Scope} scope " +
                         $"(scp: '{scopes}', aud: '{Audited("aud")}', azp: '{Audited("azp")}', " +
                         $"appid: '{Audited("appid")}').");
                return null;
            }

            return PrincipalFrom(result.Claims);
        }

        /// <summary>
        /// The principal for the claims a validated token carried: the name and the email the
        /// sign-in flow would have produced (the claim spellings <see cref="PhoneUser"/> reads,
        /// both raw and inbound-mapped), and the admin role where Entra assigned it. Building it
        /// is pure, so tests can drive it without a token at all.
        /// </summary>
        public static ClaimsPrincipal PrincipalFrom(IDictionary<string, object> claims)
        {
            var mapped = claims.Select(claim =>
            {
                var type = claim.Key switch
                {
                    "email" => ClaimTypes.Email,
                    "preferred_username" => ClaimTypes.Upn,
                    // The v1 spellings, for the tokens TeamsJS hands out (D189).
                    "upn" => ClaimTypes.Upn,
                    "name" => ClaimTypes.Name,
                    "unique_name" => ClaimTypes.Name,
                    "roles" => ClaimTypes.Role,
                    _ => claim.Key,
                };

                return new Claim(type, claim.Value?.ToString() ?? "");
            });

            return new ClaimsPrincipal(new ClaimsIdentity(mapped, "TeamsSSO"));
        }

        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(typeof(TeamsSso));
    }
}
