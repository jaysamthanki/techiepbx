using log4net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.PhoneClient
{
    /// <summary>
    /// The browser softphone at /phone (D161): the visible half of the web client PoC. The page
    /// is a dial pad and four buttons; js/phone.js registers it to Asterisk with JsSIP over the
    /// app's own WebSocket relay (D160), and the Client handler below hands the script what one
    /// extension needs to register. Deliberately not in the admin nav — it is the end-user
    /// softphone, not management surface — so its one entry point is the "Open web client" link
    /// in the extension edit modal.
    ///
    /// The folder is PhoneClient rather than Phone: a <c>Techie.Pbx.Web.*.Phone</c> namespace
    /// shadows the Core <c>Phone</c> model and breaks Pages/Phones with CS0118. The URL is still
    /// /phone, via the route template on the page.
    /// </summary>
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly ExtensionRepository extensions;

        /// <summary>Enabled extensions with the web client on: the page's dropdown.</summary>
        public List<Extension> WebExtensions { get; private set; } = new();

        public IndexModel()
        {
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
            this.WebExtensions = this.WebEnabled();
        }

        /// <summary>
        /// What the page's script needs to register ONE extension: the SIP URI of its
        /// <c>-web</c> device, the secret and the relay's WebSocket URL, all built from the
        /// request's own host and <see cref="HttpConfRenderer"/>'s constants. Anything but an
        /// enabled, web-enabled extension is a 404.
        ///
        /// The secret in the clear is the stance the extension edit form already takes (D112):
        /// /phone is an admin-only PoC page. Revisit before any non-admin user is given this
        /// page (D161).
        /// </summary>
        public async Task<IActionResult> OnGetClientAsync(string? ext)
        {
            // A GET, because fetching the registration details reads nothing but them — but the
            // response carries a SIP password, so it demands the same antiforgery token every
            // POST here carries (pbx.send always sends it). Razor Pages validates the token only
            // on unsafe verbs by itself, hence by hand.
            var antiforgery = this.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(this.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return this.BadRequest();
            }

            var extension = this.extensions.GetByNumber((ext ?? "").Trim());
            if (extension == null || !extension.Enabled || !extension.WebClient)
                return this.NotFound();

            Log.Info($"Web client registration details for {extension.Number} fetched by {this.User.Identity?.Name}");

            // ws:// when the page itself came over plain HTTP (a LAN box with no certificate
            // yet): a browser refuses a mixed-scheme WebSocket either way round.
            var scheme = this.Request.IsHttps ? "wss" : "ws";

            return new JsonResult(new
            {
                number = extension.Number,
                name = extension.Name,
                uri = $"sip:{extension.Number}{PjsipConfRenderer.WebClientSuffix}@{this.Request.Host.Host}",
                secret = extension.Secret,
                wsUrl = $"{scheme}://{this.Request.Host}{HttpConfRenderer.ProxyPath}",
            });
        }

        private List<Extension> WebEnabled() =>
            this.extensions.GetAll().Where(e => e.Enabled && e.WebClient).ToList();
    }
}
