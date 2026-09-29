using System.Text;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders http.conf: Asterisk's built-in HTTP server, which exists here for exactly one
    /// thing — the SIP WebSocket the web client registers over (D159). It serves no static
    /// files, no AJAM and no ARI, and it is enabled only while at least one enabled extension
    /// has the web client switched on: an idle HTTP server is attack surface, so a system
    /// without a web-enabled extension writes <c>enabled = no</c> and nothing listens.
    ///
    /// The TLS side is the certificate the pjsip TLS transport already uses (D101): when there
    /// is a usable one, wss is offered on <see cref="WssPort"/> from the same combined PEM the
    /// apply writes into the conf directory. Without one, plain ws on <see cref="WsPort"/> is
    /// still written so the file says honestly what is listening — browsers refuse ws from an
    /// https page, so the /phone page (piece 2) will require the wss side.
    ///
    /// http.conf is read at startup, so a change here is a restart, not a reload (D33).
    /// </summary>
    public static class HttpConfRenderer
    {
        public const string FileName = "http.conf";

        /// <summary>Where plain ws lands: Asterisk's registered HTTP port (D159).</summary>
        public const int WsPort = 8088;

        /// <summary>And wss: the HTTPS port the same server offers when it has a certificate.</summary>
        public const int WssPort = 8089;

        /// <summary>
        /// Whether the HTTP server should exist at all: only while a switched-on extension has
        /// the web client (D159). One definition, shared by this renderer and by the firewall
        /// rules that open the ports, so the two cannot disagree.
        /// </summary>
        public static bool Enabled(IEnumerable<Extension> extensions) =>
            extensions.Any(e => e.Enabled && e.WebClient);

        /// <summary>
        /// The whole file. The bind address is the one the pjsip transports bind, because both
        /// describe where this machine accepts SIP; the ports are the two constants above.
        /// </summary>
        public static string Render(
            PjsipTransport transport,
            IEnumerable<Extension> extensions,
            Certificate? certificate,
            string confDirectory)
        {
            var errors = transport.Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException("Invalid transport: " + string.Join(" ", errors));

            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[general]\n");

            if (!Enabled(extensions))
            {
                sb.Append("; No extension has the web client, so nothing listens (D159).\n");
                sb.Append("enabled = no\n");
                return sb.ToString();
            }

            sb.Append("enabled = yes\n");
            sb.Append($"bindaddr = {transport.BindAddress}\n");
            sb.Append($"bindport = {WsPort}\n");

            // The certificate is the pjsip TLS transport's, in the same combined PEM (D101).
            // http.conf has no tlsport option: the port rides on tlsbindaddr, and the key file
            // defaults to the cert file — named anyway, as the pjsip transport names both.
            if (certificate != null)
            {
                var path = ConfText.Safe(PjsipConfRenderer.TlsCertificatePath(confDirectory), "certificate path");

                sb.Append("tlsenable = yes\n");
                sb.Append($"tlsbindaddr = {transport.BindAddress}:{WssPort}\n");
                sb.Append($"tlscertfile = {path}\n");
                sb.Append($"tlsprivatekey = {path}\n");
            }

            return sb.ToString();
        }
    }
}
