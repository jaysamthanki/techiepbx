using log4net;
using Microsoft.AspNetCore.Mvc;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// The one settings action that answers with data rather than with a piece of the page:
    /// reading a stored secret back on explicit request, the way an extension's SIP password is
    /// read back (D68). Everything else about settings is a Razor Pages handler returning HTML
    /// for htmx.
    /// </summary>
    [ApiController]
    [Route("api/settings")]
    public class SettingsController : ControllerBase
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SettingsController));

        private readonly SettingsRepository settings;

        public SettingsController()
        {
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        /// <summary>
        /// The stored value of one secret setting. Only keys marked secret are served here: the
        /// rest are on the page already. The key is a query value rather than part of the path
        /// because these keys have dots in them. Who asked is logged; the value never is.
        /// </summary>
        [HttpGet("secret")]
        public IActionResult GetSecret(string? key)
        {
            var name = (key ?? "").Trim();

            if (!SettingsKeys.IsSecret(name))
                return this.NotFound(new MessageResponse("That setting has no stored secret."));

            var value = this.settings.Get(name);
            if (string.IsNullOrEmpty(value))
                return this.NotFound(new MessageResponse($"Nothing is stored for {name} yet."));

            Log.Info($"Setting {name} shown to {this.User.Identity?.Name}");
            return this.Ok(new SettingSecretResponse { Key = name, Secret = value });
        }
    }
}
