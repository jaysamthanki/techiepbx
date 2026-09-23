using log4net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Asterisk.Provisioning;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// Where Yealink desk phones fetch their configuration — the second brand on the same trust
    /// path as <see cref="PolycomController"/> (D88). DHCP option 66 points a phone at
    /// <c>http://user:pass@host:port/yealink</c>; it asks for the fixed boot file first, then its
    /// own <c>&lt;mac&gt;.cfg</c>, and both are generated from the database on the spot.
    ///
    /// Unlike Polycom, the boot request carries no MAC in its URL — the file name is fixed — so
    /// the MAC comes from the User-Agent header, and that is also where first contact
    /// auto-registration happens (D89): it is the earliest point a MAC is known at all.
    ///
    /// This is the one part of the app that is <b>not</b> behind the Entra ID cookie, and the
    /// route is deliberately plain rather than under /api so that is obvious from the URL. The
    /// same two <c>Provisioning.*</c> credentials as Polycom gate it (D88); a phone cannot sign in
    /// to Entra, so provisioning is a trust path of its own.
    ///
    /// Three answers and nothing else:
    /// <list type="bullet">
    /// <item>401 with a challenge, for credentials that are missing, wrong, or not configured.</item>
    /// <item>403, for a User-Agent that is not a phone, a phone that is switched off, a known MAC
    /// registered to a different brand, or one whose model has changed underneath us.</item>
    /// <item>404, for any file name that is not one of the two we generate.</item>
    /// </list>
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route(RouteName)]
    public class YealinkController : ControllerBase
    {
        /// <summary>
        /// The path prefix, which Program.cs also needs: provisioning answers over plain HTTP as
        /// well as HTTPS, so it is one of the paths exempt from the HTTPS redirect (D77, D88).
        /// </summary>
        public const string RoutePrefix = "/" + RouteName;

        private const string RouteName = "yealink";

        /// <summary>What a phone is told to use when Asterisk is listening on every interface.</summary>
        private const string AnyAddress = "0.0.0.0";

        private static readonly ILog Log = LogManager.GetLogger(typeof(YealinkController));

        private readonly PhoneButtonRepository buttons;
        private readonly CallFlowControlRepository callFlowControls;
        private readonly ExtensionRepository extensions;
        private readonly PhoneRepository phones;
        private readonly SettingsRepository settings;

        public YealinkController()
        {
            this.buttons = new PhoneButtonRepository(PbxDatabase.Current);
            this.callFlowControls = new CallFlowControlRepository(PbxDatabase.Current);
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.phones = new PhoneRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        /// <summary>
        /// Both provisioning files, because both are one path segment and telling them apart is
        /// this code's job rather than the router's, exactly as Polycom's controller does.
        /// </summary>
        [HttpGet("{file}")]
        public IActionResult Get(string file)
        {
            if (!this.IsAuthorized())
                return this.Challenge401();

            if (!YealinkUserAgent.TryParse(this.Request.Headers.UserAgent, out var agent))
            {
                Log.Warn($"Provisioning request for '{file}' from {this.Address()} refused: not a Yealink phone");
                return this.Forbid403("This endpoint serves Yealink phones.");
            }

            if (YealinkFiles.IsBootFile(file))
                return this.Boot(agent);

            if (YealinkFiles.TryParseConfig(file, out var mac))
                return this.Config(mac, agent);

            Log.Warn($"Provisioning request for '{file}' from {this.Address()} refused: not a file we generate");
            return this.NotFound();
        }

        /// <summary>The remote address, for the log and for the phone's own record.</summary>
        private string Address() => this.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

        /// <summary>
        /// The fixed boot file. No MAC is in the request URL, so this is also where an unknown
        /// phone is first added (D89): the User-Agent is the earliest point a MAC is known at all.
        /// </summary>
        private IActionResult Boot(YealinkUserAgent agent)
        {
            if (this.Refuse(this.phones.GetByMac(agent.Mac), agent, agent.Mac) is { } refusal)
                return refusal;

            this.phones.Register(agent.Mac, agent.Model, agent.Firmware, this.Address(), PhoneBrand.Yealink);

            return this.Text(YealinkMasterRenderer.Render(agent.Mac));
        }

        private IActionResult Challenge401()
        {
            this.Response.Headers.WWWAuthenticate = $"{BasicAuth.Scheme} realm=\"TNPBX provisioning\", charset=\"UTF-8\"";

            return this.StatusCode(StatusCodes.Status401Unauthorized);
        }

        /// <summary>
        /// The real configuration. The URL's own MAC is used for the lookup rather than the
        /// User-Agent's — if the two disagree that is just an inconsistent phone, not worth
        /// specially handling — and this request also brings the row up to date (D78, D88).
        /// </summary>
        private IActionResult Config(string mac, YealinkUserAgent agent)
        {
            var known = this.phones.GetByMac(mac);

            if (this.Refuse(known, agent, mac) is { } refusal)
                return refusal;

            var phone = this.phones.Register(mac, agent.Model, agent.Firmware, this.Address(), PhoneBrand.Yealink);
            var stored = this.settings.GetAll();
            var transport = AsteriskSettings.Transport(stored);

            // The assigned keys, and the extensions they name: the line keys are what this phone
            // registers as and the rest are its lamps (D121, schema 020). A key whose extension has
            // gone or been switched off — it would have no PJSIP endpoint — and a key on a parking
            // slot the lot no longer has are both dropped here rather than written as a
            // registration that cannot work or a lamp that can never light.
            var allExtensions = this.extensions.GetAll();
            var controls = this.callFlowControls.GetAll();
            var usable = PhoneButton.Usable(
                this.buttons.GetForPhone(phone.PhoneID), allExtensions, AsteriskSettings.Parking(stored).SlotNumbers, controls);

            var config = PhoneConfigFactory.Yealink(
                phone, usable, allExtensions, controls, stored, transport, this.Request.Scheme, this.Request.Host.Host);

            Log.Info($"Provisioning config served to {mac} ({agent.Model}) at {this.Address()}, registers as {PhoneButton.LineNumber(usable) ?? "nothing"}, {usable.Count} keys");

            return this.Text(YealinkConfigRenderer.Render(config));
        }

        private IActionResult Forbid403(string message)
        {
            return this.StatusCode(StatusCodes.Status403Forbidden, new MessageResponse(message));
        }

        /// <summary>
        /// Whether the request carries the provisioning credentials — the same two settings
        /// Polycom uses (D88). Unset credentials match nothing, so a system where nobody has
        /// filled them in serves nobody (D77).
        /// </summary>
        private bool IsAuthorized()
        {
            var stored = this.settings.GetAll();

            stored.TryGetValue(SettingsKeys.ProvisioningUsername, out var username);
            stored.TryGetValue(SettingsKeys.ProvisioningPassword, out var password);

            if (BasicAuth.Matches(this.Request.Headers.Authorization, (username ?? "").Trim(), (password ?? "").Trim()))
                return true;

            Log.Warn($"Provisioning request from {this.Address()} refused: bad or missing credentials");
            return false;
        }

        /// <summary>
        /// The three reasons a phone we already know about is turned away, or null to carry on. A
        /// brand that no longer matches means this MAC is already claimed by the other controller
        /// (D88); a model that no longer matches means either the MAC was spoofed or the hardware
        /// was swapped (D78) — both want an admin to look, not a config file.
        /// </summary>
        private IActionResult? Refuse(Phone? known, YealinkUserAgent agent, string mac)
        {
            if (known == null)
                return null;

            if (!known.Enabled)
            {
                Log.Warn($"Provisioning request for {mac} from {this.Address()} refused: the phone is disabled");
                return this.Forbid403("This phone is disabled.");
            }

            if (!known.MatchesBrand(PhoneBrand.Yealink))
            {
                Log.Warn($"Provisioning request for {mac} from {this.Address()} refused: registered as {known.Brand}, not Yealink");
                return this.Forbid403("That MAC address is registered to a different brand of phone.");
            }

            if (!known.MatchesModel(agent.Model))
            {
                Log.Warn($"Provisioning request for {mac} from {this.Address()} refused: stored model {known.Model}, User-Agent says {agent.Model}");
                return this.Forbid403("That MAC address is registered to a different model of phone.");
            }

            return null;
        }

        /// <summary>Yealink cfg files are plain text, not XML, unlike Polycom's.</summary>
        private IActionResult Text(string content) => this.Content(content, "text/plain");
    }
}
