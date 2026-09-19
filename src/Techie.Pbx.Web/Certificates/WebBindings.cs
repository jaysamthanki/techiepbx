using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Certificates
{
    /// <summary>
    /// Which ports the web app listens on, decided once at startup from the certificate rows
    /// (D99). A pure function of "is there a certificate we can serve right now", so the rule can
    /// be read and tested without starting a web server.
    ///
    /// The rule, and the reasoning behind each port:
    ///
    /// <list type="bullet">
    /// <item><b>80, always.</b> It is the only port an ACME HTTP-01 challenge is ever fetched on,
    /// so a system that is not listening there can never obtain its first certificate. Once there
    /// is one, everything on 80 except the challenge path redirects to HTTPS.</item>
    /// <item><b>443, only with a usable certificate.</b> Binding it without one would mean either
    /// a self-signed certificate nobody trusts or a port that refuses every handshake.</item>
    /// <item><b>8080, always.</b> The port every existing install, lab note and DHCP option 66 URL
    /// already names. It costs nothing to keep and it is the way back in if 80 or 443 is taken by
    /// something else.</item>
    /// </list>
    /// </summary>
    public static class WebBindings
    {
        /// <summary>The port the app has been on since the installer, kept in both modes.</summary>
        public const int BootstrapPort = 8080;

        /// <summary>Redirects, and the ACME challenge that cannot be redirected (D98).</summary>
        public const int HttpPort = 80;

        /// <summary>The real listener, once there is a certificate to present.</summary>
        public const int HttpsPort = 443;

        /// <summary>
        /// The ports to bind for a set of certificate rows. Only a certificate that is switched on,
        /// actually issued and not yet expired counts, which is the same test the TLS transport
        /// makes (D101) — so the admin UI and SIP agree about whether this system has a certificate.
        /// </summary>
        public static List<WebBinding> For(IEnumerable<Certificate> certificates, DateTimeOffset now) =>
            For(certificates.Any(certificate => certificate.IsUsable(now)));

        /// <summary>The ports to bind, given whether there is a certificate to serve.</summary>
        public static List<WebBinding> For(bool hasCertificate)
        {
            var bindings = new List<WebBinding> { new(HttpPort, UsesCertificate: false) };

            if (hasCertificate)
                bindings.Add(new WebBinding(HttpsPort, UsesCertificate: true));

            bindings.Add(new WebBinding(BootstrapPort, UsesCertificate: false));

            return bindings;
        }
    }

    /// <summary>One port the app listens on, and whether it is the HTTPS one.</summary>
    public record WebBinding(int Port, bool UsesCertificate)
    {
        /// <summary>How the binding reads in the startup log.</summary>
        public override string ToString() => $"{(this.UsesCertificate ? "https" : "http")}://0.0.0.0:{this.Port}";
    }
}
