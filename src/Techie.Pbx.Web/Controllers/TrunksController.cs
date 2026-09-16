using log4net;
using Microsoft.AspNetCore.Mvc;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// The trunk action that answers with data rather than with a piece of the page: showing the
    /// provider's password. Everything else about trunks is a Razor Pages handler returning HTML
    /// for htmx.
    ///
    /// There is no "regenerate" here, unlike extensions: the provider chooses a trunk's password,
    /// so a new random one would only break the trunk (D41).
    /// </summary>
    [ApiController]
    [Route("api/trunks")]
    public class TrunksController : ControllerBase
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(TrunksController));

        private readonly TrunkRepository trunks;

        public TrunksController()
        {
            this.trunks = new TrunkRepository(PbxDatabase.Current);
        }

        /// <summary>
        /// The password for one trunk, on explicit request. Who asked is logged; the value never is.
        /// </summary>
        [HttpGet("{trunkID:long}/secret")]
        public IActionResult GetSecret(long trunkID)
        {
            var trunk = this.trunks.GetByID(trunkID);
            if (trunk == null)
                return this.NotFound(new MessageResponse("That trunk no longer exists."));

            Log.Info($"Provider password for trunk {trunk.Name} shown to {this.User.Identity?.Name}");
            return this.Ok(new TrunkSecretResponse { Name = trunk.Name, Secret = trunk.Password });
        }
    }
}
