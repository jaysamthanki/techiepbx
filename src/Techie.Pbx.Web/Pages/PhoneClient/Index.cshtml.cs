namespace Techie.Pbx.Web.Pages.PhoneClient;

/// <summary>
/// The traditional web client at /phone (D161): a signed-in page, reached by a browser with the
/// normal Entra redirect — which works because a browser tab can navigate anywhere, unlike the
/// Teams client's iframe (D183). The Teams tab's page lives at /teams-phone, and everything the
/// two share — the extension dropdown, the Client and Unread handlers — comes from
/// <see cref="PhoneClientModel"/>.
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
public class IndexModel : PhoneClientModel
{
    public void OnGet() => this.LoadShared();
}
