using log4net;
using Microsoft.AspNetCore.Mvc;
using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// "Apply config": database to conf files to a reload of only the modules that changed.
    /// Called by our own pages with the session cookie, like every other API here.
    /// </summary>
    [ApiController]
    [Route("api/config")]
    public class ConfigController : ControllerBase
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ConfigController));

        private readonly ExtensionRepository extensions;
        private readonly InboundRouteRepository inbound;
        private readonly OutboundRouteRepository routes;
        private readonly SettingsRepository settings;
        private readonly TrunkRepository trunks;

        public ConfigController()
        {
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.inbound = new InboundRouteRepository(PbxDatabase.Current);
            this.routes = new OutboundRouteRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
            this.trunks = new TrunkRepository(PbxDatabase.Current);
        }

        [HttpPost("apply")]
        public IActionResult Apply()
        {
            var applier = ConfigApplier.FromDatabase(
                PbxDatabase.Current, this.settings, this.extensions, this.trunks, this.routes, this.inbound);

            try
            {
                var result = applier.Apply();

                Log.Info($"Apply config requested by {this.User.Identity?.Name}");
                return this.Ok(new ApplyConfigResponse { Summary = Summarise(result) });
            }
            catch (AmiException ex)
            {
                // The files are written by this point; only the reload failed.
                Log.Error($"Apply config: the files were written but Asterisk could not be reloaded: {ex.Message}", ex);
                return this.StatusCode(StatusCodes.Status502BadGateway, new MessageResponse(
                    "The config files were written, but Asterisk could not be reloaded over AMI. Check that Asterisk is running and that the AMI settings are right."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Apply config: could not write the config files: {ex.Message}", ex);
                return this.StatusCode(StatusCodes.Status500InternalServerError, new MessageResponse(
                    "The config files could not be written. Check that the conf directory exists and that the web user may write to it."));
            }
            catch (InvalidOperationException ex)
            {
                // A renderer refused the data it was given: missing AMI credentials, a setting
                // that contradicts the generated config, or a row that would break out of a conf
                // file. The message is ours and says which, so it is worth showing.
                Log.Error($"Apply config: the config could not be rendered: {ex.Message}", ex);
                return this.StatusCode(StatusCodes.Status500InternalServerError, new MessageResponse(
                    "The config could not be generated: " + ex.Message));
            }
        }

        private static string Summarise(ApplyResult result)
        {
            if (result.ChangedFiles.Count == 0)
                return "Nothing to do: the config files already match the database.";

            var summary = $"Wrote {string.Join(", ", result.ChangedFiles)}";

            summary += result.ReloadedModules.Count > 0
                ? $" and reloaded {string.Join(", ", result.ReloadedModules)}."
                : ".";

            // Saying "done" when Asterisk is still running the old file would be a lie (D33).
            if (result.RestartRequired)
            {
                summary += $" Asterisk must be restarted before {string.Join(", ", result.RestartRequiredFiles)} " +
                           "take effect; it is still running the config it started with.";
            }

            return summary;
        }
    }
}
