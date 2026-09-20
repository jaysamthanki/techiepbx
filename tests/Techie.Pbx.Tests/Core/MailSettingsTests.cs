using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Mail;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Reading the Mail.* keys, and the one decision <see cref="MailSettings"/> makes: what a blank
    /// transport resolves to (D115). No network and no database — it is a map in and an answer out.
    /// </summary>
    public class MailSettingsTests
    {
        [Fact]
        public void Nothing_stored_is_no_transport_when_graph_has_no_credential()
        {
            Assert.Equal(MailSettings.NoTransport, Settings().Effective(graphAvailable: false));
        }

        /// <summary>
        /// The whole point of "blank": a site already on Microsoft 365 gets Graph without having to
        /// know that is what it wanted.
        /// </summary>
        [Fact]
        public void Nothing_stored_is_graph_when_graph_has_a_credential()
        {
            Assert.Equal(MailTransports.Graph, Settings().Effective(graphAvailable: true));
        }

        /// <summary>
        /// A stored choice wins even when it will not work. An admin who chose SMTP and left the
        /// host blank should be told SMTP is not configured, not have their mail quietly sent
        /// another way.
        /// </summary>
        [Fact]
        public void A_stored_transport_wins_over_what_is_available()
        {
            var settings = Settings((SettingsKeys.MailTransport, MailTransports.Smtp));

            Assert.Equal(MailTransports.Smtp, settings.Effective(graphAvailable: true));
        }

        [Fact]
        public void A_transport_nobody_recognises_is_treated_as_blank()
        {
            var settings = Settings((SettingsKeys.MailTransport, "carrier-pigeon"));

            Assert.Equal(MailSettings.NoTransport, settings.Effective(graphAvailable: false));
            Assert.Equal(MailTransports.Graph, settings.Effective(graphAvailable: true));
        }

        [Fact]
        public void The_smtp_port_defaults_to_submission_and_ignores_nonsense()
        {
            Assert.Equal(MailSettings.DefaultSmtpPort, Settings().SmtpPort);
            Assert.Equal(MailSettings.DefaultSmtpPort, Settings((SettingsKeys.MailSmtpPort, "ninety")).SmtpPort);
            Assert.Equal(MailSettings.DefaultSmtpPort, Settings((SettingsKeys.MailSmtpPort, "70000")).SmtpPort);
            Assert.Equal(2525, Settings((SettingsKeys.MailSmtpPort, "2525")).SmtpPort);
        }

        [Fact]
        public void Values_are_trimmed_and_missing_ones_are_blank()
        {
            var settings = Settings((SettingsKeys.MailFromAddress, "  pbx@example.com  "));

            Assert.Equal("pbx@example.com", settings.FromAddress);
            Assert.Equal("", settings.FromName);
            Assert.Equal("", settings.SmtpHost);
            Assert.Equal("", settings.SmtpPassword);
            Assert.Equal("", settings.SmtpUsername);
        }

        private static MailSettings Settings(params (string Key, string Value)[] stored) =>
            new(stored.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
    }
}
