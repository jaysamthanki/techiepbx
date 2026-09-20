using log4net;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// The one way mail leaves this box. It picks the transport (D115) and hands the message to it;
    /// the two transports know nothing about each other and nothing about settings resolution.
    ///
    /// Everything it can answer with is a <see cref="MailResult"/>, including "there is no
    /// transport": a system that cannot send mail should say so plainly rather than throw, because
    /// the caller is a button an admin just pressed.
    /// </summary>
    public class MailSender
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(MailSender));

        private readonly GraphCredential? graph;
        private readonly MailSettings settings;

        /// <summary>Whether Graph has a credential to send with at all.</summary>
        public bool GraphAvailable => this.graph is { IsComplete: true };

        /// <summary>
        /// The transport a send would use right now, which is what the Email tab shows so an admin
        /// can see what "blank" resolved to without sending anything.
        /// </summary>
        public string Transport => this.settings.Effective(this.GraphAvailable);

        public MailSender(MailSettings settings, GraphCredential? graph)
        {
            this.graph = graph;
            this.settings = settings;
        }

        /// <summary>
        /// Sends one alert, rendered into the D114 template. The result is the whole answer: it
        /// either went to a transport that accepted it, or it says what is missing.
        /// </summary>
        public Task<MailResult> SendAsync(string to, AlertEmail alert, CancellationToken cancellationToken) =>
            this.SendAsync(to, alert.Subject, AlertEmailRenderer.Render(alert), cancellationToken);

        /// <summary>Sends one HTML message that has already been rendered.</summary>
        public async Task<MailResult> SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken)
        {
            var address = (to ?? "").Trim();

            if (address.Length == 0)
                return MailResult.Failed("No address to send to.");

            var transport = this.Transport;

            Log.Info($"Sending mail to {address} over '{(transport.Length > 0 ? transport : "none")}'");

            return transport switch
            {
                MailTransports.Graph => await new GraphMailSender(this.settings, this.graph).SendAsync(address, subject, htmlBody, cancellationToken),
                MailTransports.Smtp => await new SmtpMailSender(this.settings).SendAsync(address, subject, htmlBody, cancellationToken),

                // Blank, and no Entra app credential to fall back on. Saying which two ways out
                // there are beats "not configured" on its own.
                _ => MailResult.Failed(
                    "This system has no way to send mail yet. On the Email tab, either set the transport to SMTP and " +
                    "fill in the relay details, or configure this app's Entra client secret on the server so Graph can send."),
            };
        }
    }
}
