using log4net;
using Microsoft.AspNetCore.Mvc;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Mail;
using Techie.Pbx.Web.Security;

namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// The "send test mail" button behind the Email tab (D115), and nothing else: this app sends
    /// no mail on its own yet, so the one endpoint here exists to prove the settings are right
    /// before anything depends on them.
    ///
    /// Called by our own page with the session cookie, like every other API here.
    /// </summary>
    [ApiController]
    [Route("api/mail")]
    public class MailController : ControllerBase
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(MailController));

        private readonly SettingsRepository settings;

        public MailController()
        {
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        /// <summary>
        /// Sends one message to an address the admin just typed, over whatever transport the
        /// settings resolve to, and answers with what happened either way.
        ///
        /// A refusal is a 502 with our own sentence rather than an exception: getting mail working
        /// is a conversation between the admin and their relay or their tenant, and every answer
        /// here is a step in it. The sentence never carries a credential.
        /// </summary>
        [HttpPost("test")]
        public async Task<IActionResult> Test([FromBody] TestMailRequest request, CancellationToken cancellationToken)
        {
            var to = (request?.To ?? "").Trim();

            if (to.Length == 0)
                return this.BadRequest(new MessageResponse("Type an address to send the test to."));

            var sender = new MailSender(new MailSettings(this.settings.GetAll()), PbxEntra.Credential);
            var who = SignedInUser.DisplayName(this.User);

            Log.Info($"Test mail to {to} requested by {this.User.Identity?.Name}");

            var result = await sender.SendAsync(to, Alert(who), cancellationToken);

            if (!result.Success)
                return this.StatusCode(StatusCodes.Status502BadGateway, new MessageResponse(result.Message));

            return this.Ok(new MessageResponse(result.Message));
        }

        /// <summary>
        /// What the test message says. Deliberately the ordinary alert template (D114) rather than
        /// a bare line of text: the point of the test is to see what a real alert will look like
        /// when it lands, including whether the recipient's client renders the card at all.
        /// </summary>
        private static AlertEmail Alert(string who) => new()
        {
            AccentColor = AlertEmail.AccentInfo,
            Body =
            {
                "This is a test message from your TNPBX system.",
                "",
                "If you are reading it, the mail settings on the Email tab work: this server can reach its transport, the transport accepted the message, and it arrived here.",
            },
            Details =
            {
                new AlertEmailDetail("Sent by", who.Length > 0 ? who : "an administrator"),
                new AlertEmailDetail("Server", Environment.MachineName),
            },
            Hostname = Environment.MachineName,
            Subject = "TNPBX test message",
            Timestamp = DateTimeOffset.Now.ToString("dddd d MMMM yyyy, HH:mm:ss zzz"),
        };
    }
}
