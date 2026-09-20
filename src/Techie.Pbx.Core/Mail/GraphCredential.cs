namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// The app-only half of this application's own Entra registration — the same tenant and client
    /// it signs admins in with, plus the client secret that registration already needs to redeem an
    /// authorization code. No second app and no credential of our own invention (D115).
    ///
    /// The secret is never in this repository: it lives in the server's appsettings or user secrets
    /// like every other credential, so on a machine where nobody has configured one
    /// <see cref="IsComplete"/> is false and Graph simply reports itself unconfigured.
    ///
    /// Having the credential is still not the same as being allowed to use it: sending needs the
    /// <c>Mail.Send</c> <b>application</b> permission with admin consent on that registration,
    /// which is a change in Entra that no code here can make. Graph says so when it is missing.
    /// </summary>
    public class GraphCredential
    {
        public string ClientId { get; }

        /// <summary>A secret. Never logged, never returned to the browser.</summary>
        public string ClientSecret { get; }

        /// <summary>Whether there is enough here to ask Entra for a token at all.</summary>
        public bool IsComplete =>
            this.ClientId.Length > 0 && this.ClientSecret.Length > 0 && this.TenantId.Length > 0;

        public string TenantId { get; }

        public GraphCredential(string? tenantId, string? clientId, string? clientSecret)
        {
            this.ClientId = (clientId ?? "").Trim();
            this.ClientSecret = (clientSecret ?? "").Trim();
            this.TenantId = (tenantId ?? "").Trim();
        }
    }
}
