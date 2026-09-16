using System.Net;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Web.Security
{
    /// <summary>
    /// The failsafe sign-in (D24): when Entra cannot be reached, or on the lab VM where there is
    /// no app registration for the address the browser uses, requests from a listed network are
    /// treated as signed in. Off unless someone turns it on, and never wider than the networks
    /// they list.
    /// </summary>
    public class LocalBypassSettings
    {
        /// <summary>Marks the identity as ours, so the UI can say so and logs can be read.</summary>
        public const string AuthenticationType = "LocalBypass";

        public const string IdentityName = "local-bypass";
        public const string SectionName = "LocalAuthenticationBypass";

        public Ipv4Networks AllowedNetworks { get; }
        public bool Enabled { get; }

        public LocalBypassSettings(bool enabled, IEnumerable<string>? allowedNetworks)
        {
            this.Enabled = enabled;
            this.AllowedNetworks = new Ipv4Networks(allowedNetworks);
        }

        /// <summary>
        /// Reads the LocalAuthenticationBypass section. A missing section, a missing Enabled and a
        /// missing list all mean off: the bypass has to be asked for, never assumed.
        /// </summary>
        public static LocalBypassSettings FromConfiguration(IConfiguration configuration)
        {
            var section = configuration.GetSection(SectionName);

            return new LocalBypassSettings(
                section.GetValue("Enabled", false),
                section.GetSection("AllowedNetworks").Get<string[]>());
        }

        public bool Allows(IPAddress? remoteAddress) => this.Enabled && this.AllowedNetworks.Contains(remoteAddress);
    }
}
