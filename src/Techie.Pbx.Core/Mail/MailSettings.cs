using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// The Mail.* settings, read once out of the settings map and handed around as one object —
    /// the same shape the Asterisk-side settings objects have, so "how does this system send mail"
    /// is one answer rather than seven lookups scattered about (D115).
    ///
    /// Nothing here reaches the network. It only says what was configured and what that resolves
    /// to; <see cref="MailSender"/> is what acts on it.
    /// </summary>
    public class MailSettings
    {
        /// <summary>
        /// The submission port with STARTTLS, which is what every relay in the help text uses.
        /// Not 25: that is the port between mail servers, and no hosted relay accepts it from here.
        /// </summary>
        public const int DefaultSmtpPort = 587;

        /// <summary>What <see cref="Effective"/> answers when no transport is usable at all.</summary>
        public const string NoTransport = "";

        public string FromAddress { get; }

        public string FromName { get; }

        public string SmtpHost { get; }

        /// <summary>A secret. Never logged, never rendered into a page outside the edit form (D112).</summary>
        public string SmtpPassword { get; }

        public int SmtpPort { get; }

        public string SmtpUsername { get; }

        /// <summary>
        /// The transport as stored, which may be blank. <see cref="Effective"/> is what resolves
        /// blank into an actual answer; this is only what the admin chose.
        /// </summary>
        public string Transport { get; }

        public MailSettings(IReadOnlyDictionary<string, string> stored)
        {
            this.FromAddress = Value(stored, SettingsKeys.MailFromAddress);
            this.FromName = Value(stored, SettingsKeys.MailFromName);
            this.SmtpHost = Value(stored, SettingsKeys.MailSmtpHost);
            this.SmtpPassword = Value(stored, SettingsKeys.MailSmtpPassword);
            this.SmtpPort = Number(Value(stored, SettingsKeys.MailSmtpPort), DefaultSmtpPort);
            this.SmtpUsername = Value(stored, SettingsKeys.MailSmtpUsername);
            this.Transport = Value(stored, SettingsKeys.MailTransport);
        }

        /// <summary>
        /// Which transport a send would actually use. A stored choice wins, whether or not it will
        /// work — an admin who chose SMTP and left the host blank should be told SMTP is not
        /// configured, not quietly have their mail sent some other way.
        ///
        /// Blank is "decide for me": Graph when this app has an Entra app credential to send with,
        /// and <see cref="NoTransport"/> when it has not, because there is then nothing to guess
        /// with (D115).
        /// </summary>
        public string Effective(bool graphAvailable)
        {
            if (MailTransports.IsKnown(this.Transport))
                return this.Transport;

            return graphAvailable ? MailTransports.Graph : NoTransport;
        }

        private static int Number(string value, int fallback) =>
            int.TryParse(value, out var number) && number is >= 1 and <= 65535 ? number : fallback;

        private static string Value(IReadOnlyDictionary<string, string> stored, string key) =>
            stored.TryGetValue(key, out var value) ? value.Trim() : "";
    }
}
