using log4net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Security;

namespace Techie.Pbx.Web.Pages.PhoneClient;

/// <summary>
/// What the softphone pages share: the extension dropdown's data, the Client handler that hands
/// js/phone.js one extension's registration details, and the Unread handler behind the voicemail
/// badge. Both /phone (the traditional web client, D161) and /teams-phone (the Teams tab's page,
/// D183) inherit this; each page decides for itself who may reach it.
/// </summary>
public abstract class PhoneClientModel : PageModel
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PhoneClientModel));

    protected readonly ExtensionRepository Extensions;
    protected readonly SettingsRepository Settings;

    /// <summary>Whether the user holds the admin role: decides what an empty page says.</summary>
    public bool IsAdmin { get; private set; }

    /// <summary>
    /// The page's dropdown: the enabled, web-enabled extensions this user may open — all of
    /// them for an admin, their own for anyone else (D164).
    /// </summary>
    public List<Extension> WebExtensions { get; private set; } = new();

    protected PhoneClientModel()
    {
        this.Extensions = new ExtensionRepository(PbxDatabase.Current);
        this.Settings = new SettingsRepository(PbxDatabase.Current);
    }

    /// <summary>Fills the shared properties a GET must show: the role and the extension list.</summary>
    protected void LoadShared()
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
    /// answer is the same as one that does not exist.
    ///
    /// The secret stays in the clear, because the softphone needs it to REGISTER, but it is
    /// only ever served to the extension's owner or to an admin — who can read it on the
    /// extension edit form anyway (D112).
    /// </summary>
    public async Task<IActionResult> OnGetClientAsync(string? ext)
    {
        // Both pages serve the registration details to whoever their page reached them; an
        // anonymous caller (only /teams-phone allows one at all) gets the same answer as a
        // nonexistent extension — nothing leaks to the shell.
        if (this.User.Identity?.IsAuthenticated != true)
        {
            return this.Unauthorized();
        }

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
        {
            return this.NotFound();
        }

        Log.Info($"Web client registration details for {extension.Number} fetched by {this.User.Identity?.Name}");

        // ws:// when the page itself came over plain HTTP (a LAN box with no certificate
        // yet): a browser refuses a mixed-scheme WebSocket either way round.
        var scheme = this.Request.IsHttps ? "wss" : "ws";

        return new JsonResult(new
        {
            number = extension.Number,
            name = extension.Name,
            uri = $"sip:{extension.Number}{PjsipConfRenderer.WebClientSuffix}@{this.Request.Host.Host}",
            wsUrl = $"{scheme}://{this.Request.Host}{HttpConfRenderer.ProxyPath}",
            secret = extension.Secret,
        });
    }

    /// <summary>
    /// The Voicemail button's badge: the unread messages across the signed-in user's own
    /// voicemail mailboxes (D169), counted on the spool. No parameter on purpose — the
    /// mailboxes come from the sign-in alone (<see cref="PhoneUser.Mailboxes"/>). A user who
    /// owns no voicemail mailbox gets a 404, which the page treats as no badge.
    /// </summary>
    public IActionResult OnGetUnread()
    {
        if (this.User.Identity?.IsAuthenticated != true)
        {
            return this.Unauthorized();
        }

        var mailboxes = PhoneUser.Mailboxes(this.User, this.Extensions.GetAll());
        if (mailboxes.Count == 0)
        {
            return this.NotFound();
        }

        return new JsonResult(new { unread = VoicemailInbox.Unread(VoicemailSpool.Root, VoicemailConfRenderer.MailboxContext, mailboxes) });
    }

    private List<Extension> Allowed() => PhoneUser.Extensions(this.User, this.Extensions.GetAll());
}
