using System.Net;
using log4net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Mail;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// Where the <c>voicemail-mail</c> script asks this application to send the voicemail email
    /// itself (D129): our own template, the transcript inside it, and the recording attached as an
    /// MP3 — rather than the generic MIME message app_voicemail composes.
    ///
    /// <para><b>The token is the only thing protecting this endpoint</b>, which is why checking it
    /// is the first thing the method does. It is not behind the Entra cookie, because the caller is
    /// a script running as the asterisk user with no browser and no session; it is
    /// <c>Mail.VoicemailCallbackToken</c>, generated at startup, written into
    /// <c>Config/mail.json</c> beside the relay password and read from there by the script. A blank
    /// token matches nothing at all, so a system that has not got one has a closed endpoint rather
    /// than an open one. The loopback check behind it is a second wall, not the first: only a
    /// process on this machine can reach this at all.</para>
    ///
    /// <para><b>Nothing here is trusted.</b> The mailbox has to be an extension with voicemail and
    /// an address; the path has to be exactly the shape app_voicemail writes, for that same mailbox
    /// (<see cref="VoicemailSpool"/>); and the caller's name and number are text a stranger chose,
    /// so they are flattened before they reach a subject line and HTML-encoded before they reach a
    /// body.</para>
    ///
    /// <para><b>Every failure is a status code the script can act on.</b> Anything but 200 means
    /// "the app could not do it", and the script then relays what app_voicemail composed exactly as
    /// it did before this existed (D126, D128). An application that is down, broken or mid-deploy
    /// must never mean a voicemail nobody hears.</para>
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Route(VoicemailCallback.Route)]
    public class VoicemailNotifyController : ControllerBase
    {
        /// <summary>The longest caller name or number that reaches an email.</summary>
        private const int MaxCallerLength = 96;

        /// <summary>
        /// The longest transcript that reaches an email. Five minutes of speech is a few thousand
        /// characters; this is where something has gone wrong with the model rather than where a
        /// talkative caller is cut off.
        /// </summary>
        private const int MaxTranscriptLength = 20000;

        private static readonly ILog Log = LogManager.GetLogger(typeof(VoicemailNotifyController));

        private readonly ExtensionRepository extensions;
        private readonly SettingsRepository settings;

        public VoicemailNotifyController()
        {
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        /// <summary>
        /// Composes and sends one voicemail email. 200 and the script is done; anything else and
        /// the script falls back to relaying the message itself.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Notify([FromBody] VoicemailNotifyRequest request, CancellationToken cancellationToken)
        {
            var mail = new MailSettings(this.settings.GetAll());

            // First, before anything else is read or opened: this token is the whole gate.
            if (!BearerToken.Matches(this.Request.Headers.Authorization, mail.VoicemailCallbackToken))
            {
                Log.Warn($"Voicemail notify from {this.Address()} refused: the bearer token did not match");
                return this.Unauthorized();
            }

            var remote = this.HttpContext.Connection.RemoteIpAddress;

            if (remote == null || !IPAddress.IsLoopback(remote))
            {
                Log.Warn($"Voicemail notify from {this.Address()} refused: it did not come from this machine");
                return this.StatusCode(StatusCodes.Status403Forbidden, new MessageResponse("This endpoint answers this machine only."));
            }

            var mailbox = (request?.Mailbox ?? "").Trim();

            if (!Extension.IsValidNumber(mailbox))
            {
                Log.Warn("Voicemail notify refused: the mailbox is not an extension number");
                return this.BadRequest(new MessageResponse("That is not a mailbox number."));
            }

            if (VoicemailSpool.Problem(mailbox, request?.MessagePath) is { } problem)
            {
                Log.Warn($"Voicemail notify for mailbox {mailbox} refused: {problem}");
                return this.BadRequest(new MessageResponse("That is not a voicemail message path."));
            }

            var extension = this.extensions.GetByNumber(mailbox);

            if (extension == null || !extension.VoicemailEnabled || extension.VoicemailEmail.Length == 0)
            {
                Log.Warn($"Voicemail notify for mailbox {mailbox} refused: no extension with voicemail and an email address");
                return this.NotFound(new MessageResponse("No mailbox here emails anybody."));
            }

            var voicemail = Compose(extension, request!);
            var attachment = Attachment(extension, request!.MessagePath, voicemail.ReceivedAt);

            voicemail.RecordingAttached = attachment != null;

            var result = await new VoicemailEmailSender(mail).SendAsync(
                extension.VoicemailEmail, voicemail, attachment, cancellationToken);

            if (!result.Success)
            {
                // The sentence is ours and carries no credential, but it is written for an admin
                // rather than for a script, so it goes to the log and a short reason goes back.
                Log.Error($"Voicemail email for mailbox {mailbox} was not sent: {result.Message}");
                return this.StatusCode(StatusCodes.Status502BadGateway, new MessageResponse("The relay would not take the message."));
            }

            Log.Info($"Voicemail email for mailbox {mailbox} sent to {extension.VoicemailEmail}");
            return this.Ok(new VoicemailNotifyResponse(true));
        }

        /// <summary>The remote address, for the log.</summary>
        private string Address() => this.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

        /// <summary>
        /// The recording to hang on the message: an MP3 where ffmpeg could make one, the file as
        /// Asterisk wrote it where it could not, and nothing at all for a mailbox that asked to be
        /// emailed without the audio. A conversion that fails never fails the email (D129).
        /// </summary>
        private static MailAttachment? Attachment(Extension extension, string messagePath, DateTimeOffset receivedAt)
        {
            if (!extension.VoicemailAttachRecording)
                return null;

            var recording = new VoicemailRecording();
            var mp3 = recording.Mp3(messagePath);
            var audio = mp3 ?? recording.Original(messagePath);

            if (audio == null)
                return null;

            if (mp3 == null)
                Log.Warn($"The recording for mailbox {extension.Number} could not be converted, so the original is attached instead");

            return VoicemailEmailSender.Attachment(extension.Number, receivedAt, audio, mp3 != null);
        }

        /// <summary>
        /// What the email will say. The transcript is fetched here because it is part of the
        /// message rather than part of the delivery, and because a mailbox that did not ask for one
        /// must not pay for the attempt.
        /// </summary>
        private static VoicemailEmail Compose(Extension extension, VoicemailNotifyRequest request)
        {
            var transcript = extension.VoicemailTranscribe
                ? new VoicemailRecording().Transcript(request.MessagePath)
                : null;

            return new VoicemailEmail
            {
                CallerId = MailText.Plain(request.CallerId, MaxCallerLength),
                CallerName = MailText.Plain(request.CallerName, MaxCallerLength),
                DurationSeconds = request.DurationSeconds,
                Hostname = Environment.MachineName,
                Mailbox = extension.Number,
                MailboxName = extension.Name,
                ReceivedAt = Received(request.ReceivedEpoch),
                Transcript = transcript == null ? null : MailText.Block(transcript, MaxTranscriptLength),
            };
        }

        /// <summary>
        /// When the message arrived, in this server's local time. A missing or absurd epoch — a
        /// header app_voicemail did not write, or wrote in a form nobody expected — becomes now,
        /// which is within a second or two of the truth anyway.
        /// </summary>
        private static DateTimeOffset Received(long epochSeconds)
        {
            if (epochSeconds <= 0 || epochSeconds > DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds())
                return DateTimeOffset.Now;

            return DateTimeOffset.FromUnixTimeSeconds(epochSeconds).ToLocalTime();
        }
    }
}
