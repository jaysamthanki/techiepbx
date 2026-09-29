using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// http.conf (D159): Asterisk's HTTP server exists for the web client's SIP WebSocket and
    /// for nothing else, so it is enabled exactly while an enabled extension has the web client,
    /// offers wss exactly while the pjsip TLS transport's certificate exists, and says
    /// enabled = no otherwise.
    /// </summary>
    public class HttpConfRendererTests
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-28T12:00:00Z");

        private static Certificate Usable() => new()
        {
            Name = "pbx",
            Hostnames = "pbx.example.com",
            CertificatePem = "-----BEGIN CERTIFICATE-----",
            KeyPem = "-----BEGIN PRIVATE KEY-----",
            ExpiresUtc = Now.AddDays(60).ToString("u"),
        };

        private static List<Extension> WithWebClient() => new()
        {
            new Extension { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
            new Extension { Number = "1002", Name = "Sales", Secret = "EEEEffffGGGGhhhh2222", WebClient = true },
        };

        private static List<Extension> WithoutWebClient() => new()
        {
            new Extension { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        private static string Render(List<Extension> extensions, Certificate? certificate) =>
            HttpConfRenderer.Render(new PjsipTransport(), extensions, certificate, "/etc/asterisk");

        [Fact]
        public void Http_with_a_certificate_matches_expected_file()
        {
            Assert.Equal(Expected("http-tls.conf"), Render(WithWebClient(), Usable()));
        }

        [Fact]
        public void Http_without_a_certificate_matches_expected_file()
        {
            Assert.Equal(Expected("http.conf"), Render(WithWebClient(), null));
        }

        [Fact]
        public void Http_is_disabled_when_no_extension_has_the_web_client()
        {
            var actual = Render(WithoutWebClient(), Usable());

            Assert.Equal(Expected("http-disabled.conf"), actual);
            Assert.DoesNotContain("enabled = yes", actual);
            Assert.DoesNotContain("tlsenable", actual);
        }

        /// <summary>A switched-off extension renders no endpoint, so it opens no listener either.</summary>
        [Fact]
        public void A_disabled_extension_with_the_web_client_does_not_enable_the_server()
        {
            var extensions = new List<Extension>
            {
                new()
                {
                    Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111",
                    WebClient = true, Enabled = false,
                },
            };

            Assert.False(HttpConfRenderer.Enabled(extensions));
            Assert.Contains("enabled = no\n", Render(extensions, null));
        }

        /// <summary>The HTTP server binds where the SIP transports bind: both describe this machine.</summary>
        [Fact]
        public void The_bind_address_is_the_sip_transports_bind_address()
        {
            var transport = new PjsipTransport { BindAddress = "10.8.20.8" };

            var actual = HttpConfRenderer.Render(transport, WithWebClient(), Usable(), "/etc/asterisk");

            Assert.Contains("bindaddr = 10.8.20.8\n", actual);
            Assert.Contains($"tlsbindaddr = 10.8.20.8:{HttpConfRenderer.WssPort}\n", actual);
        }

        /// <summary>
        /// The certificate the WebSocket presents is the one file the apply already writes for
        /// the pjsip TLS transport (D101): nothing new for an operator to copy into place.
        /// </summary>
        [Fact]
        public void The_certificate_is_the_pjsip_transports_combined_pem()
        {
            var actual = Render(WithWebClient(), Usable());

            Assert.Contains($"tlscertfile = {PjsipConfRenderer.TlsCertificatePath("/etc/asterisk")}\n", actual);
            Assert.Contains($"tlsprivatekey = {PjsipConfRenderer.TlsCertificatePath("/etc/asterisk")}\n", actual);
        }

        [Fact]
        public void Http_conf_is_a_restart_not_a_reload()
        {
            var file = new GeneratedFile(HttpConfRenderer.FileName, null, Render(WithoutWebClient(), null));

            Assert.True(file.NeedsRestart);
            Assert.Empty(ConfigApplier.ReloadOrder(new List<GeneratedFile> { file }));
        }
    }
}
