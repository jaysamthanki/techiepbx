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
    /// Where desk phones fetch their configuration. DHCP option 160 points a phone at
    /// <c>http://user:pass@host/polycom</c>, it asks for <c>&lt;mac&gt;.cfg</c> and then
    /// <c>exten&lt;mac&gt;.cfg</c>, and both are generated from the database on the spot (D77, D79).
    ///
    /// This is the one part of the app that is <b>not</b> behind the Entra ID cookie, and the route
    /// is deliberately plain rather than under /api so that is obvious from the URL. A phone cannot
    /// sign in to Entra, so provisioning is a trust path of its own: HTTP Basic against two settings
    /// keys, a User-Agent that has to look like a Polycom phone, and a MAC address that has to be
    /// twelve hex digits before anything is looked up (D77).
    ///
    /// Three answers and nothing else:
    /// <list type="bullet">
    /// <item>401 with a challenge, for credentials that are missing, wrong, or not configured.</item>
    /// <item>403, for a User-Agent that is not a phone, a phone that is switched off, or a known MAC
    /// whose model has changed underneath us.</item>
    /// <item>404, for any file name that is not one of the two we generate.</item>
    /// </list>
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route(RouteName)]
    public class PolycomController : ControllerBase
    {
        /// <summary>
        /// The path prefix, which Program.cs also needs: provisioning answers over plain HTTP as
        /// well as HTTPS, so it is the one path exempt from the HTTPS redirect (D77).
        /// </summary>
        public const string RoutePrefix = "/" + RouteName;

        private const string RouteName = "polycom";

        /// <summary>What a phone is told to use when it asks for the time or for SIP.</summary>
        private const string AnyAddress = "0.0.0.0";

        private static readonly ILog Log = LogManager.GetLogger(typeof(PolycomController));

        private readonly ExtensionRepository extensions;
        private readonly PhoneRepository phones;
        private readonly SettingsRepository settings;

        public PolycomController()
        {
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.phones = new PhoneRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        /// <summary>
        /// Both provisioning files, because both are one path segment and telling them apart is
        /// this code's job rather than the router's: a MAC address is a strict pattern, and a
        /// routing template that matched loosely would hand a lookup something it should not.
        /// </summary>
        [HttpGet("{file}")]
        public IActionResult Get(string file)
        {
            if (!this.IsAuthorized())
                return this.Challenge401();

            if (!PolycomUserAgent.TryParse(this.Request.Headers.UserAgent, out var agent))
            {
                Log.Warn($"Provisioning request for '{file}' from {this.Address()} refused: not a Polycom phone");
                return this.Forbid403("This endpoint serves Polycom phones.");
            }

            if (PolycomFiles.TryParseMaster(file, out var masterMac))
                return this.Master(masterMac, agent);

            if (PolycomFiles.TryParseConfig(file, out var configMac))
                return this.Config(configMac, agent);

            Log.Warn($"Provisioning request for '{file}' from {this.Address()} refused: not a file we generate");
            return this.NotFound();
        }

        /// <summary>The remote address, for the log and for the phone's own record.</summary>
        private string Address() => this.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

        private IActionResult Challenge401()
        {
            this.Response.Headers.WWWAuthenticate = $"{BasicAuth.Scheme} realm=\"TNPBX provisioning\", charset=\"UTF-8\"";

            return this.StatusCode(StatusCodes.Status401Unauthorized);
        }

        /// <summary>
        /// The real configuration, and the only place a phone can create or update its own row:
        /// an unknown MAC is auto-added with what its User-Agent said, a known one is brought up
        /// to date (D78).
        /// </summary>
        private IActionResult Config(string mac, PolycomUserAgent agent)
        {
            var known = this.phones.GetByMac(mac);

            if (this.Refuse(known, agent, mac) is { } refusal)
                return refusal;

            var phone = this.phones.Register(mac, agent.Model, agent.Firmware, this.Address());
            var stored = this.settings.GetAll();
            var transport = AsteriskSettings.Transport(stored);

            // An extension that has been switched off has no PJSIP endpoint to register against,
            // so the phone is given the unassigned file rather than credentials that cannot work.
            var extension = phone.ExtensionID is > 0 ? this.extensions.GetByID(phone.ExtensionID.Value) : null;
            if (extension is { Enabled: false })
                extension = null;

            var config = new PolycomConfig
            {
                Extension = extension,
                GmtOffsetSeconds = PolycomConfig.GmtOffsetFor(AsteriskSettings.Timezone(stored)),
                Phone = phone,
                ServerAddress = this.ServerAddress(transport.BindAddress),
                SipPort = transport.Port,
                SntpAddress = this.RequestHost(),
            };

            Log.Info($"Provisioning config served to {mac} ({agent.Model}) at {this.Address()}, extension {extension?.Number ?? "none"}");

            return this.Xml(PolycomConfigRenderer.Render(config));
        }

        private IActionResult Forbid403(string message)
        {
            return this.StatusCode(StatusCodes.Status403Forbidden, new MessageResponse(message));
        }

        /// <summary>
        /// Whether the request carries the provisioning credentials. Unset credentials match
        /// nothing, so a system where nobody has filled them in serves nobody (D77).
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
        /// The master file, which names the config file and holds nothing else. An unknown MAC is
        /// answered with one: it carries no credentials, and the phone is added when it comes back
        /// for the file this points at (D78).
        /// </summary>
        private IActionResult Master(string mac, PolycomUserAgent agent)
        {
            if (this.Refuse(this.phones.GetByMac(mac), agent, mac) is { } refusal)
                return refusal;

            return this.Xml(PolycomMasterRenderer.Render(mac));
        }

        /// <summary>
        /// The two reasons a phone we already know about is turned away, or null to carry on. A
        /// model that no longer matches means either the MAC was spoofed or the hardware was
        /// swapped; both want an admin to look, not a config file (D78).
        /// </summary>
        private IActionResult? Refuse(Phone? known, PolycomUserAgent agent, string mac)
        {
            if (known == null)
                return null;

            if (!known.Enabled)
            {
                Log.Warn($"Provisioning request for {mac} from {this.Address()} refused: the phone is disabled");
                return this.Forbid403("This phone is disabled.");
            }

            if (!known.MatchesModel(agent.Model))
            {
                Log.Warn($"Provisioning request for {mac} from {this.Address()} refused: stored model {known.Model}, User-Agent says {agent.Model}");
                return this.Forbid403("That MAC address is registered to a different model of phone.");
            }

            return null;
        }

        /// <summary>The host the phone asked us on, without the port it asked on.</summary>
        private string RequestHost() => this.Request.Host.Host;

        /// <summary>
        /// Where the phone should send SIP: the bind address, unless Asterisk is listening on every
        /// interface, in which case the only address we can honestly name is the one the phone just
        /// reached us on.
        /// </summary>
        private string ServerAddress(string bindAddress) =>
            bindAddress.Length == 0 || bindAddress == AnyAddress ? this.RequestHost() : bindAddress;

        private IActionResult Xml(string content) => this.Content(content, "text/xml");
    }
}
