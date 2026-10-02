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
    /// The server binds LOOPBACK ONLY (D160, amending D159): the browser never reaches it
    /// directly. The web app terminates TLS on 443 and relays the WebSocket itself, so the
    /// browser's SIP connection is <c>wss://<hostname>/asterisk-ws</c> on a port that is
    /// already open, and the deployment's firewall never needs a hole for a second HTTP port —
    /// on this lab the cloud firewall allows only 80, 443, the media range and a few chosen
    /// ports, and that is typical of deployments generally. Loopback traffic crosses no
    /// firewall at all, so there is also no TLS half to this file: the certificate the browser
    /// sees is the web app's own (D99), one cert, one place.
    ///
    /// http.conf is read at startup, so a change here is a restart, not a reload (D33).
    /// </summary>
    public static class HttpConfRenderer
    {
        public const string FileName = "http.conf";

        /// <summary>Where the loopback ws lands: Asterisk's registered HTTP port (D159).</summary>
        public const int WsPort = 8088;

        /// <summary>The only address the HTTP server ever binds (D160): the app relays for it.</summary>
        public const string LoopbackAddress = "127.0.0.1";

        /// <summary>
        /// The path the web app maps its WebSocket relay on — the address the browser's SIP
        /// WebSocket dials (D160). Lives here so the relay, the renderer's comments and the
        /// /phone page (piece 2) cannot drift apart on what the URL is.
        /// </summary>
        public const string ProxyPath = "/asterisk-ws";

        /// <summary>
        /// The Asterisk side of the relay: the loopback WebSocket the web app connects to and
        /// forwards bytes both ways (D160). res_http_websocket answers every upgrade of /ws.
        /// Spelled as a literal because a const string may not concatenate the port's number;
        /// the renderer test holds it to the same address http.conf binds.
        /// </summary>
        public const string UpstreamUri = "ws://127.0.0.1:8088/ws";

        /// <summary>
        /// The subprotocol Asterisk's WebSocket transport insists on: a handshake without
        /// <c>Sec-WebSocket-Protocol: sip</c> is refused with 400 (verified live on the lab),
        /// and it is the only protocol this system speaks over the socket.
        /// </summary>
        public const string SipSubProtocol = "sip";

        /// <summary>
        /// Whether the HTTP server should exist at all: only while a switched-on extension has
        /// the web client (D159). One definition, shared by this renderer, the web app's relay
        /// and the apply that writes the file, so the config and what is listening cannot
        /// disagree.
        /// </summary>
        public static bool Enabled(IEnumerable<Extension> extensions) =>
            extensions.Any(e => e.Enabled && e.WebClient);

        /// <summary>
        /// The whole file. Loopback and one port, nothing else: TLS is the web app's job, so
        /// there is no certificate half to this file and nothing for an operator to keep in
        /// step (D160).
        /// </summary>
        public static string Render(IEnumerable<Extension> extensions)
        {
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
            sb.Append($"bindaddr = {LoopbackAddress}\n");
            sb.Append($"bindport = {WsPort}\n");

            return sb.ToString();
        }
    }
}
