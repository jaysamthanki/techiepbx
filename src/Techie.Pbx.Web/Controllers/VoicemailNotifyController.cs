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
    /// (<see cref="VoicemailSpool"/>); the recording posted in the body (D162) has to decode
    /// within <see cref="VoicemailRecording.MaxInlineBytes"/> and name a format that is letters
    /// and digits before either goes near a file; and the caller's name and number are text a
    /// stranger chose, so they are flattened before they reach a subject line and HTML-encoded
    /// before they reach a body.</para>
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

            // The recording may travel in the body (D162): a mailbox with delete=yes has no spool
            // files left by the time this request is answered, so the posted bytes are the copy
            // that cannot be raced. A field that is present but not a recording is a bad request,
            // not a fallback — a script that filled it in meant to send one.
            byte[]? inlineAudio = null;
            string? inlineFormat = null;

            if ((request?.RecordingBase64 ?? "").Length > 0)
            {
                inlineAudio = VoicemailRecording.InlineBytes(request!.RecordingBase64);
                inlineFormat = VoicemailRecording.SafeFormat(request.RecordingFormat);

                if (inlineAudio == null || inlineFormat == null)
                {
                    Log.Warn($"Voicemail notify for mailbox {mailbox} refused: the posted recording is not base64 within the cap, or names an odd format");
                    return this.BadRequest(new MessageResponse("That is not a recording."));
                }
            }

            // A path is still held to the only shape app_voicemail writes whenever one is sent;
            // it is only allowed to be absent when the recording came inline instead (D162).
            var messagePath = request?.MessagePath ?? "";

            if ((messagePath.Length > 0 || inlineAudio == null)
                && VoicemailSpool.Problem(mailbox, messagePath) is { } problem)
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

            var recording = new VoicemailRecording();
            var voicemail = Compose(extension, request!, messagePath,
                Transcript(extension, recording, inlineAudio, inlineFormat, messagePath));
            var attachment = Attachment(extension, recording, inlineAudio, inlineFormat, messagePath, voicemail.ReceivedAt);

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
        /// The recording to hang on the message: the posted bytes when the script sent them,
        /// because they cannot be raced by delete=yes (D162), and the spool otherwise. Either way
        /// it is an MP3 where ffmpeg could make one, the recording as it came where it could not,
        /// and nothing at all for a mailbox that asked to be emailed without the audio. A
        /// conversion that fails never fails the email (D129). Public and handed its pieces so
        /// the order of preference is a tested decision rather than glue.
        /// </summary>
        public static MailAttachment? Attachment(Extension extension, VoicemailRecording recording,
            byte[]? inlineAudio, string? inlineFormat, string messagePath, DateTimeOffset receivedAt)
        {
            if (!extension.VoicemailAttachRecording)
                return null;

            var mp3 = inlineAudio != null
                ? recording.Mp3(inlineAudio, inlineFormat ?? "")
                : recording.Mp3(messagePath);
            var audio = mp3 ?? inlineAudio ?? (messagePath.Length > 0 ? recording.Original(messagePath) : null);

            if (audio == null)
                return null;

            if (mp3 == null)
                Log.Warn($"The recording for mailbox {extension.Number} could not be converted, so the original is attached instead");

            return VoicemailEmailSender.Attachment(extension.Number, receivedAt, audio, mp3 != null);
        }

        /// <summary>
        /// What the caller said, for a mailbox that asked — one that did not must not pay for the
        /// attempt. The posted bytes are preferred over the spool for the same reason the
        /// attachment prefers them: with delete=yes there is no spool to read (D162). Public and
        /// handed its pieces so the order of preference is a tested decision rather than glue.
        /// </summary>
        public static string? Transcript(Extension extension, VoicemailRecording recording,
            byte[]? inlineAudio, string? inlineFormat, string messagePath)
        {
            if (!extension.VoicemailTranscribe)
                return null;

            return inlineAudio != null
                ? recording.Transcript(inlineAudio, inlineFormat ?? "")
                : recording.Transcript(messagePath);
        }

        /// <summary>
        /// What the email will say. The transcript arrives ready-made because it is part of the
        /// message rather than part of the delivery.
        /// </summary>
        private static VoicemailEmail Compose(Extension extension, VoicemailNotifyRequest request,
            string messagePath, string? transcript)
        {
            // The sidecar is what app_voicemail actually wrote; the request's header-derived
            // values only fill the gaps when it is missing (D129) — and with no path at all
            // (delete=yes, D162) they are all there is.
            var facts = messagePath.Length > 0 ? VoicemailRecording.Facts(messagePath) : null;

            return new VoicemailEmail
            {
                CallerId = MailText.Plain(facts?.CallerId.Length > 0 ? facts.CallerId : request.CallerId, MaxCallerLength),
                CallerName = MailText.Plain(facts?.CallerName.Length > 0 ? facts.CallerName : request.CallerName, MaxCallerLength),
                DurationSeconds = facts?.DurationSeconds > 0 ? facts.DurationSeconds : request.DurationSeconds,
                Hostname = Environment.MachineName,
                Mailbox = extension.Number,
                MailboxName = extension.Name,
                ReceivedAt = Received(facts?.ReceivedEpoch > 0 ? facts.ReceivedEpoch : request.ReceivedEpoch),
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
