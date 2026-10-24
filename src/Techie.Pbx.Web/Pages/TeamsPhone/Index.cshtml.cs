using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Pages.TeamsPhone;

/// <summary>
/// The Teams tab's softphone at /teams-phone (D183): the same dialer as /phone, but this page is
/// the one the Teams client's iframe loads, and that iframe cannot run the normal Entra redirect
/// (Microsoft's login pages refuse to be framed). So an anonymous caller gets the SSO shell —
/// its script asks Teams for a token for this app's own API and posts it to the exchange
/// endpoint — and the signed-in reload that follows lands on the real phone.
///
/// [AllowAnonymous] is what lets the Teams tab's iframe get here at all; an anonymous caller
/// sees the shell and nothing else, because the shared handlers fail closed with 401. Once
/// signed in this page is exactly as strict as /phone: the one page a signed-in non-admin may
/// reach (D165), extensions matched to their sign-in address (D164).
/// </summary>
[AllowAnonymous]
public class IndexModel : Pages.PhoneClient.PhoneClientModel
{
    private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(typeof(IndexModel));

    private readonly IConfiguration configuration;

    /// <summary>
    /// Whether the page must render the Teams SSO shell instead of the phone: nobody is signed
    /// in, so the real page's data is not theirs to see. In a plain browser the shell offers
    /// the ordinary sign-in link, which a browser tab can follow even though the iframe cannot.
    /// </summary>
    public bool ShowSsoShell { get; private set; }

    /// <summary>The Application ID URI the Teams token is requested for (api://host/clientId).</summary>
    public string SsoResource => $"api://{this.SiteHost}/{this.SsoClientId}";

    /// <summary>
    /// The Teams client ID and site host the SSO flow uses: the TeamsApp.* settings where they
    /// were set for the manifest, falling back to this app's own Entra registration and host.
    /// </summary>
    public string SsoClientId { get; private set; } = "";
    public string SiteHost { get; private set; } = "";

    public IndexModel(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public async Task OnGetAsync()
    {
        // The Teams loop diagnosis (D195): whether the session cookie the exchange issued ever
        // comes back on the reload, and when it does, what the cookie handler itself says about
        // it — AuthenticateAsync surfaces the exact refusal (bad key, bad payload, no ticket)
        // that the middleware only swallows. Names and lengths only, never values.
        var probe = await this.HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        Log.Info($"teams-phone auth probe: {(probe.Succeeded ? "succeeded" : probe.None ? "none" : "failed")}" +
                 (probe.Failure is { } failure ? $", failure: {failure.Message}" : "") +
                 (probe.Ticket?.Principal.Identity?.Name is { } name ? $", ticket for: {name}" : ""));
        Log.Info($"teams-phone GET: {this.Request.Cookies.Count} cookie(s) " +
                 $"[{string.Join(", ", this.Request.Cookies.Keys.OrderBy(name => name))}], " +
                 $"auth cookie {(this.Request.Cookies.ContainsKey(".AspNetCore.Cookies") ? this.Request.Cookies[".AspNetCore.Cookies"]!.Length + " chars" : "absent")}, " +
                 $"authenticated: {this.User.Identity?.IsAuthenticated == true}");

        if (this.User.Identity?.IsAuthenticated == true)
        {
            this.LoadShared();
            return;
        }

        this.ShowSsoShell = true;
        var stored = this.Settings.GetAll();
        this.SsoClientId = stored.GetValueOrDefault(SettingsKeys.TeamsAppClientId)
            ?? this.configuration["AzureAd:ClientId"] ?? "";
        this.SiteHost = stored.GetValueOrDefault(SettingsKeys.TeamsAppSiteUrl)
            ?? this.Request.Host.Host;
    }
}
