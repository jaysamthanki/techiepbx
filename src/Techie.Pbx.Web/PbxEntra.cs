using log4net;
using Techie.Pbx.Core.Mail;

namespace Techie.Pbx.Web
{
    /// <summary>
    /// This application's own Entra registration, read once at startup — the same
    /// <c>AzureAd</c> section that signs admins in, seen from the app-only side so Graph can send
    /// mail as a mailbox in the tenant (D115). A static holder for the same reason
    /// <see cref="PbxDatabase"/> and <see cref="PbxSounds"/> are ones (D22): things are built with
    /// <c>new</c> rather than taken from the container.
    ///
    /// The client secret is configuration, never repository content: appsettings on the server, or
    /// user secrets in development. A machine where nobody set one gets an incomplete credential,
    /// and Graph reports itself unconfigured rather than pretending (D115).
    /// </summary>
    public static class PbxEntra
    {
        public const string ClientIdSetting = "AzureAd:ClientId";

        public const string ClientSecretSetting = "AzureAd:ClientSecret";

        public const string TenantIdSetting = "AzureAd:TenantId";

        private static readonly ILog Log = LogManager.GetLogger(typeof(PbxEntra));

        private static GraphCredential credential = new(null, null, null);

        /// <summary>
        /// The app-only credential. Never null: an unconfigured one answers
        /// <see cref="GraphCredential.IsComplete"/> false, which is what callers ask.
        /// </summary>
        public static GraphCredential Credential => credential;

        /// <summary>
        /// Reads the registration out of configuration. Logs whether there is an app-only
        /// credential at all, and never what it is.
        /// </summary>
        public static void Open(IConfiguration configuration)
        {
            credential = new GraphCredential(
                configuration[TenantIdSetting],
                configuration[ClientIdSetting],
                configuration[ClientSecretSetting]);

            Log.Info(credential.IsComplete
                ? "Entra app-only credential is configured: Microsoft Graph can send mail once Mail.Send is consented"
                : $"No Entra app-only credential ({ClientSecretSetting} is not set): Microsoft Graph cannot send mail");
        }
    }
}
