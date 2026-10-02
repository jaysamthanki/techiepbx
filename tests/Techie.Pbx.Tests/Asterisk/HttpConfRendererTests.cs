using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// http.conf (D159, amended by D160): Asterisk's HTTP server exists for the web client's
    /// SIP WebSocket and for nothing else, so it is enabled exactly while an enabled extension
    /// has the web client — and it binds loopback only, because the browser never reaches it:
    /// the web app relays the WebSocket over its own HTTPS port.
    /// </summary>
    public class HttpConfRendererTests
    {
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

        private static string Render(List<Extension> extensions) => HttpConfRenderer.Render(extensions);

        [Fact]
        public void Http_with_a_web_client_matches_expected_file()
        {
            Assert.Equal(Expected("http.conf"), Render(WithWebClient()));
        }

        [Fact]
        public void Http_is_disabled_when_no_extension_has_the_web_client()
        {
            var actual = Render(WithoutWebClient());

            Assert.Equal(Expected("http-disabled.conf"), actual);
            Assert.DoesNotContain("enabled = yes", actual);
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
            Assert.Contains("enabled = no\n", Render(extensions));
        }

        /// <summary>
        /// Loopback and nothing else (D160): the server is bound behind the app's relay, so a
        /// bind address that anything but this machine could reach is a mistake.
        /// </summary>
        [Fact]
        public void The_server_binds_loopback_only()
        {
            var actual = Render(WithWebClient());

            Assert.Contains($"bindaddr = {HttpConfRenderer.LoopbackAddress}\n", actual);
            Assert.Contains($"bindport = {HttpConfRenderer.WsPort}\n", actual);
        }

        /// <summary>
        /// No TLS half at all (D160): the certificate the browser sees is the web app's own, so
        /// http.conf must not name a certificate or a second port even in passing.
        /// </summary>
        [Fact]
        public void No_certificate_or_wss_port_is_rendered()
        {
            var actual = Render(WithWebClient());

            Assert.DoesNotContain("tls", actual);
            Assert.DoesNotContain("8089", actual);
        }

        /// <summary>
        /// The relay's upstream address names the port the file binds (D160): the URI is a
        /// literal, so nothing but this test holds it to the loopback address and port that
        /// http.conf actually writes.
        /// </summary>
        [Fact]
        public void The_relay_upstream_uri_names_the_bound_port_and_address()
        {
            Assert.Equal(
                $"ws://{HttpConfRenderer.LoopbackAddress}:{HttpConfRenderer.WsPort}/ws",
                HttpConfRenderer.UpstreamUri);
        }

        [Fact]
        public void Http_conf_is_a_restart_not_a_reload()
        {
            var file = new GeneratedFile(HttpConfRenderer.FileName, null, Render(WithoutWebClient()));

            Assert.True(file.NeedsRestart);
            Assert.Empty(ConfigApplier.ReloadOrder(new List<GeneratedFile> { file }));
        }
    }
}
