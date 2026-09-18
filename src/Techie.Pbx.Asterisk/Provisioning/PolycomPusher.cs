using System.Net;
using System.Net.Http.Headers;
using System.Text;
using log4net;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// Tells a Polycom phone, over its own web UI, to fetch its config again or to reboot — the
    /// mechanism this app's save button and "Reboot phone" use so an admin does not have to wait
    /// for the daily poll (D79) or walk to the desk (D84).
    ///
    /// Best-effort by design: a phone that is off, on another network, or slow to answer still
    /// gets the config at its next poll regardless, so a failed push is a warning, never an
    /// exception a caller has to handle.
    /// </summary>
    public static class PolycomPusher
    {
        /// <summary>The account Polycom's own firmware calls "Polycom", the one local admin login.</summary>
        private const string PushUsername = "Polycom";

        /// <summary>Long enough for a phone on the same network to answer, short enough not to hang a save.</summary>
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

        private static readonly ILog Log = LogManager.GetLogger(typeof(PolycomPusher));

        /// <summary>
        /// Pushes a reboot. <paramref name="handler"/> is how a test replaces the network call;
        /// production callers leave it null and get a real one.
        /// </summary>
        public static Task<bool> PushReboot(string ip, string adminPassword, HttpMessageHandler? handler = null) =>
            Push(ip, adminPassword, "Action:Reboot", handler);

        /// <summary>
        /// Pushes a config reload — the phone re-fetches and applies its files immediately instead
        /// of waiting for its next poll.
        /// </summary>
        public static Task<bool> PushUpdateConfig(string ip, string adminPassword, HttpMessageHandler? handler = null) =>
            Push(ip, adminPassword, "Action:UpdateConfig", handler);

        /// <summary>
        /// The phone's web UI has no CA-signed certificate to offer — it is always self-signed —
        /// so the only question worth asking is "does this answer as Polycom's admin account?",
        /// which the digest challenge itself settles.
        /// </summary>
        private static HttpMessageHandler DefaultHandler(string adminPassword) => new HttpClientHandler
        {
            Credentials = new NetworkCredential(PushUsername, adminPassword),
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };

        /// <summary>Returns whether the phone accepted the push; never throws.</summary>
        private static async Task<bool> Push(string ip, string adminPassword, string action, HttpMessageHandler? handler)
        {
            if (ip.Length == 0)
                return false;

            using var client = new HttpClient(handler ?? DefaultHandler(adminPassword), disposeHandler: handler == null)
            {
                Timeout = RequestTimeout,
            };

            var body = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><PolycomIPPhone><Data priority=\"Critical\">{action}</Data></PolycomIPPhone>";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{ip}/push")
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/xml"),
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/push-simpleui"));

                using var response = await client.SendAsync(request).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return true;

                Log.Warn($"Push to phone at {ip} answered {(int)response.StatusCode} {response.ReasonPhrase}");
                return false;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                Log.Warn($"Push to phone at {ip} failed: {ex.Message}");
                return false;
            }
        }
    }
}
