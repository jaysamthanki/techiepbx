using System.Net.WebSockets;
using System.Threading.Tasks;
using log4net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Services
{
    /// <summary>
    /// The browser-facing half of the web client's SIP WebSocket (D160, amending D159).
    ///
    /// Asterisk's HTTP server binds loopback only, so the browser cannot dial it: deployments
    /// open 80 and 443 for the web app and a chosen handful of other ports, and a second HTTP
    /// port for Asterisk is exactly the surface this project does not keep. Instead the app
    /// relays: the browser opens <c>wss://<hostname>/asterisk-ws</c> on the web app's own HTTPS
    /// port, and every frame is forwarded byte-for-byte to Asterisk's loopback WebSocket and
    /// back. TLS is therefore the web app's certificate (D99), one cert in one place, and
    /// Asterisk serves nothing to the network at all.
    ///
    /// A relay, not a parser: the app never looks inside the SIP frames. The upstream address
    /// and the path both come from <see cref="HttpConfRenderer"/>, which also writes the file
    /// the server it points at is configured from, so the three cannot drift apart.
    ///
    /// Authorized like any page (fallback policy): the WebSocket handshake is a same-origin
    /// request from the /phone page and carries the session cookie, so the same Entra sign-in
    /// (or local bypass subnet) that reached the page reaches the socket.
    /// </summary>
    public static class AsteriskWebSocketRelay
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(AsteriskWebSocketRelay));

        private const int BufferSize = 8192;

        /// <summary>Maps the relay path. Call once at startup, after the auth middleware.</summary>
        public static void Map(WebApplication app)
        {
            app.Map(HttpConfRenderer.ProxyPath, RelayAsync);
        }

        private static async Task RelayAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // The same gate http.conf is written by (D159): with no web-enabled extension there
            // is no listener to relay to, and saying so plainly beats a proxy that always 502s.
            var extensions = new ExtensionRepository(PbxDatabase.Current);
            if (!HttpConfRenderer.Enabled(extensions.GetAll()))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // Ends the pump that is still mid-receive when the other half finishes: without
            // this, the surviving pump holds the sockets open after one side is gone.
            using var ending = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

            using var browser = await context.WebSockets.AcceptWebSocketAsync();
            using var upstream = new ClientWebSocket();

            try
            {
                await upstream.ConnectAsync(new Uri(HttpConfRenderer.UpstreamUri), ending.Token);
            }
            catch (Exception ex)
            {
                // Most often "Asterisk is down": the socket works, the server behind it does not.
                Log.Warn($"Web client relay: Asterisk refused the loopback WebSocket: {ex.Message}");
                await CloseAsync(browser, WebSocketCloseStatus.EndpointUnavailable, "Asterisk is unreachable");
                return;
            }

            Log.Info("Web client relay: WebSocket connected");

            // Two one-way pumps; whichever finishes first ends both. Frames are forwarded as
            // they arrived — same type, no merge, no copy beyond the buffer — because SIP over
            // WebSocket is a stream of discrete frames and both ends depend on that shape.
            var toUpstream = PumpAsync(browser, upstream, ending.Token);
            var toBrowser = PumpAsync(upstream, browser, ending.Token);

            await Task.WhenAny(toUpstream, toBrowser);
            ending.Cancel();

            await CloseAsync(browser, WebSocketCloseStatus.NormalClosure, "Relay ended");
            await CloseAsync(upstream, WebSocketCloseStatus.NormalClosure, "Relay ended");

            Log.Info("Web client relay: WebSocket closed");
        }

        /// <summary>A close that never throws: by the time it runs, one half is usually torn
        /// down already, and the relay is over whatever the other half says about it.</summary>
        private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string reason)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.CloseAsync(status, reason, CancellationToken.None);
            }
            catch
            {
            }
        }

        private static async Task PumpAsync(WebSocket from, WebSocket to, CancellationToken cancelled)
        {
            var buffer = new byte[BufferSize];

            while (true)
            {
                WebSocketReceiveResult received;

                using var message = new MemoryStream();
                do
                {
                    received = await from.ReceiveAsync(new ArraySegment<byte>(buffer), cancelled);

                    if (received.MessageType == WebSocketMessageType.Close)
                        return;

                    message.Write(buffer, 0, received.Count);
                }
                while (!received.EndOfMessage);

                var frame = message.ToArray();
                await to.SendAsync(new ArraySegment<byte>(frame), received.MessageType, true, cancelled);
            }
        }
    }
}
