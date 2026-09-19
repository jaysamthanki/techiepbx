using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The TLS transport (D101): it exists in pjsip.conf exactly when there is a usable
    /// certificate to point it at, and its certificate and key are the one combined file the
    /// apply wrote — so nothing an operator copies into place.
    /// </summary>
    public class TlsTransportTests
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-22T12:00:00Z");

        private static string Render(Certificate? certificate, PjsipTransport transport) =>
            PjsipConfRenderer.Render(transport, new Extension[0], new Trunk[0], certificate, "/etc/asterisk");

        private static Certificate Usable() => new()
        {
            Name = "pbx",
            Hostnames = "pbx.example.com",
            CertificatePem = "-----BEGIN CERTIFICATE-----",
            KeyPem = "-----BEGIN PRIVATE KEY-----",
            ExpiresUtc = Now.AddDays(60).ToString("u"),
        };

        private static Certificate Expired() => new()
        {
            Name = "pbx",
            Hostnames = "pbx.example.com",
            CertificatePem = "-----BEGIN CERTIFICATE-----",
            KeyPem = "-----BEGIN PRIVATE KEY-----",
            ExpiresUtc = Now.AddDays(-1).ToString("u"),
        };

        // The renderer's null means "no certificate": whether a row counts as usable is decided
        // before it gets here, by CertificateRepository.Current — an expired certificate never
        // reaches Render at all. So this test is about the null the renderer is handed.
        [Fact]
        public void A_null_certificate_means_no_tls_transport()
        {
            var actual = Render(null, new PjsipTransport());

            Assert.DoesNotContain(PjsipConfRenderer.TlsTransportName, actual);
            Assert.DoesNotContain("protocol = tls", actual);
            Assert.DoesNotContain(PjsipConfRenderer.TlsCertificateFileName, actual);
        }

        private static string RenderUsable(PjsipTransport transport) => Render(Usable(), transport);

        [Fact]
        public void A_usable_certificate_gets_a_tls_transport_on_the_default_port()
        {
            var actual = RenderUsable(new PjsipTransport());

            Assert.Contains(
                $"[{PjsipConfRenderer.TlsTransportName}]\ntype = transport\nprotocol = tls\nbind = 0.0.0.0:5061\n",
                actual);
        }

        [Fact]
        public void The_tls_transport_binds_the_tls_port_setting()
        {
            var actual = RenderUsable(new PjsipTransport { TlsPort = 5071 });

            Assert.Contains("protocol = tls\nbind = 0.0.0.0:5071\n", actual);
        }

        [Fact]
        public void Certificate_and_key_are_the_one_combined_file_in_the_conf_directory()
        {
            var actual = RenderUsable(new PjsipTransport());

            Assert.Contains($"cert_file = {PjsipConfRenderer.TlsCertificatePath("/etc/asterisk")}\n", actual);
            Assert.Contains($"priv_key_file = {PjsipConfRenderer.TlsCertificatePath("/etc/asterisk")}\n", actual);
        }
    }
}
