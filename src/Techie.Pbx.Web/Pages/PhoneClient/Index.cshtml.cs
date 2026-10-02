using log4net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Security;

namespace Techie.Pbx.Web.Pages.PhoneClient
{
    /// <summary>
    /// The browser softphone at /phone (D161): the visible half of the web client PoC. The page
    /// is a dial pad and four buttons; js/phone.js registers it to Asterisk with JsSIP over the
    /// app's own WebSocket relay (D160), and the Client handler below hands the script what one
    /// extension needs to register. Deliberately not in the admin nav — it is the end-user
    /// softphone, not management surface — so an admin's one entry point is the "Open web
    /// client" link in the extension edit modal.
    ///
    /// The one page a signed-in user without the admin role may reach (D165). An admin sees
    /// every web-enabled extension; anyone else sees only the extensions whose voicemail email
    /// is the address they signed in with (D164). <see cref="PhoneUser"/> decides, for the
    /// dropdown and for the handler alike.
    ///
    /// The folder is PhoneClient rather than Phone: a <c>Techie.Pbx.Web.*.Phone</c> namespace
    /// shadows the Core <c>Phone</c> model and breaks Pages/Phones with CS0118. The URL is still
    /// /phone, via the route template on the page.
    /// </summary>
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly ExtensionRepository extensions;
        private readonly SettingsRepository settings;

        /// <summary>Whether the user holds the admin role: decides what an empty page says.</summary>
        public bool IsAdmin { get; private set; }

        /// <summary>
        /// The page's dropdown: the enabled, web-enabled extensions this user may open — all of
        /// them for an admin, their own for anyone else.
        /// </summary>
        public List<Extension> WebExtensions { get; private set; } = new();

        public IndexModel()
        {
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
            this.IsAdmin = AdminRole.IsAdmin(this.User);
            this.WebExtensions = this.Allowed();
        }

        /// <summary>
        /// What the page's script needs to register ONE extension: the SIP URI of its
        /// <c>-web</c> device, the secret and the relay's WebSocket URL, all built from the
        /// request's own host and <see cref="HttpConfRenderer"/>'s constants. Anything but an
        /// enabled, web-enabled extension this user may open is a 404 — for a non-admin, that
        /// is every extension whose voicemail email is not their sign-in address, and the
        /// answer is the same as for one that does not exist.
        ///
        /// The secret stays in the clear, because the softphone needs it to REGISTER, but it is
        /// only ever served to the extension's owner or to an admin — who can read it on the
        /// extension edit form anyway (D112). That closes D161's "revisit before any non-admin
        /// user is given this page" (D164).
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

            var number = (ext ?? "").Trim();
            var extension = this.Allowed().FirstOrDefault(e => e.Number == number);
            if (extension == null)
                return this.NotFound();

            Log.Info($"Web client registration details for {extension.Number} fetched by {this.User.Identity?.Name}");

            // ws:// when the page itself came over plain HTTP (a LAN box with no certificate
            // yet): a browser refuses a mixed-scheme WebSocket either way round.
            var scheme = this.Request.IsHttps ? "wss" : "ws";

            // The same STUN the transport uses (D163): the browser's candidates need it before
            // this crosses a NAT, and one setting serves both. Unset stays off — no STUN for
            // Asterisk means none for the browser either, which is right on a LAN or VPN.
            var stun = this.settings.Get(SettingsKeys.SipStunServer);

            return new JsonResult(new
            {
                number = extension.Number,
                name = extension.Name,
                uri = $"sip:{extension.Number}{PjsipConfRenderer.WebClientSuffix}@{this.Request.Host.Host}",
                secret = extension.Secret,
                wsUrl = $"{scheme}://{this.Request.Host}{HttpConfRenderer.ProxyPath}",
                stun = string.IsNullOrEmpty(stun) ? null : $"stun:{stun}",
            });
        }

        /// <summary>
        /// The Voicemail button's badge: the unread messages across the signed-in user's own
        /// voicemail mailboxes (D169), counted on the spool. No parameter on purpose — the
        /// mailboxes come from the sign-in alone (<see cref="PhoneUser.Mailboxes"/>), so there is
        /// nothing to ask about anybody else's. A user who owns no voicemail mailbox gets a 404,
        /// which the page treats the same as zero: no badge.
        /// </summary>
        public IActionResult OnGetUnread()
        {
            var mailboxes = PhoneUser.Mailboxes(this.User, this.extensions.GetAll());
            if (mailboxes.Count == 0)
                return this.NotFound();

            var unread = VoicemailInbox.Unread(VoicemailSpool.Root, VoicemailConfRenderer.MailboxContext, mailboxes);

            return new JsonResult(new { unread });
        }

        private List<Extension> Allowed() => PhoneUser.Extensions(this.User, this.extensions.GetAll());
    }
}
