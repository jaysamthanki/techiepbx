using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using log4net;

namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// Submission to an SMTP relay — SendGrid, Google Workspace, or the site's own (D115).
    /// <see cref="SmtpClient"/> from the BCL rather than another package: this sends one small
    /// message at a time to a relay that does the real work, which is the job it is still good at.
    ///
    /// STARTTLS always. Submission over plain text would put the relay password on the wire, and
    /// every relay worth using on port 587 offers TLS.
    /// </summary>
    public class SmtpMailSender
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SmtpMailSender));

        private readonly MailSettings settings;

        public SmtpMailSender(MailSettings settings)
        {
            this.settings = settings;
        }

        /// <summary>
        /// Sends one HTML message, or says why it could not. Nothing here throws at the caller:
        /// a relay refusing mail is an ordinary thing for an admin to have to fix.
        /// </summary>
        public Task<MailResult> SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken) =>
            this.SendAsync(to, subject, htmlBody, null, cancellationToken);

        /// <summary>
        /// The same send with a file on it — the voicemail recording, and nothing else so far
        /// (D129). Bytes rather than a path: this class never opens a file somebody named.
        /// </summary>
        public async Task<MailResult> SendAsync(string to, string subject, string htmlBody, MailAttachment? attachment, CancellationToken cancellationToken)
        {
            if (this.settings.SmtpHost.Length == 0)
                return MailResult.Failed("SMTP is the chosen transport, but no SMTP host is set. Set Mail.Smtp.Host on the Email tab first.");

            if (this.settings.FromAddress.Length == 0)
                return MailResult.Failed("No from address is set. Set Mail.FromAddress on the Email tab: a relay will not accept mail from nobody.");

            var where = $"{this.settings.SmtpHost}:{this.settings.SmtpPort}";

            try
            {
                using var client = new SmtpClient(this.settings.SmtpHost, this.settings.SmtpPort)
                {
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    EnableSsl = true,
                    UseDefaultCredentials = false,
                };

                // No username means the relay is expected to trust this host by address, which is
                // how an internal relay is usually set up. With one, it is the password's only use.
                if (this.settings.SmtpUsername.Length > 0)
                    client.Credentials = new NetworkCredential(this.settings.SmtpUsername, this.settings.SmtpPassword);

                using var message = new MailMessage
                {
                    Body = htmlBody,
                    IsBodyHtml = true,
                    Subject = subject,
                };

                message.From = this.settings.FromName.Length > 0
                    ? new MailAddress(this.settings.FromAddress, this.settings.FromName)
                    : new MailAddress(this.settings.FromAddress);

                message.To.Add(to);

                // The Attachment owns the stream and the message owns the Attachment, so disposing
                // the message — which the using above does — closes both.
                if (attachment != null)
                {
                    message.Attachments.Add(new Attachment(
                        new MemoryStream(attachment.Bytes),
                        attachment.FileName,
                        attachment.ContentType));
                }

                await client.SendMailAsync(message, cancellationToken);

                // The relay, the recipient and nothing else. The password is never in a log line.
                Log.Info($"Mail sent over SMTP through {where} to {to}");
                return MailResult.Sent($"Sent to {to} over SMTP through {where}.");
            }
            catch (FormatException ex)
            {
                Log.Warn($"SMTP send to {to} refused: an address could not be parsed: {ex.Message}");
                return MailResult.Failed($"'{to}' is not an address this can send to. Check it, and check the from address on the Email tab.");
            }
            catch (SmtpException ex)
            {
                Log.Warn($"SMTP send to {to} through {where} failed: {ex.StatusCode} {ex.Message}");
                return MailResult.Failed(
                    $"The relay at {where} refused the message ({ex.StatusCode}). Check the host, port, username and password — " +
                    "and that this server is allowed to submit mail through it.");
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
            {
                Log.Warn($"SMTP send to {to} through {where} failed: {ex.Message}");
                return MailResult.Failed($"Could not talk to the relay at {where}. Check the host and port, and that this server may reach it.");
            }
            catch (OperationCanceledException)
            {
                Log.Warn($"SMTP send to {to} through {where} timed out");
                return MailResult.Failed($"The relay at {where} did not answer in time.");
            }
        }
    }
}
