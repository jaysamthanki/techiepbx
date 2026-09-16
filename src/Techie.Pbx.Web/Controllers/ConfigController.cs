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
        private readonly SettingsRepository settings;

        public ConfigController()
        {
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        [HttpPost("apply")]
        public IActionResult Apply()
        {
            var applier = ConfigApplier.FromDatabase(PbxDatabase.Current, this.settings, this.extensions);

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
        }

        private static string Summarise(ApplyResult result)
        {
            if (result.ChangedFiles.Count == 0)
                return "Nothing to do: the config files already match the database.";

            return $"Wrote {string.Join(", ", result.ChangedFiles)} and reloaded {string.Join(", ", result.ReloadedModules)}.";
        }
    }
}
