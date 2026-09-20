using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Mail;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// What the senders do before they would touch the network (D115). Every case here is one they
    /// refuse outright, which is exactly the set that can be tested without a relay or a tenant —
    /// and the set that matters most, because it is what an admin sees while setting mail up.
    /// </summary>
    public class MailSenderTests
    {
        /// <summary>
        /// Named here rather than referenced: the constant lives in the web project, which this
        /// test project does not reference, and the message an admin reads has to name it.
        /// </summary>
        private const string PbxEntraSecretSetting = "AzureAd:ClientSecret";

        [Fact]
        public async Task With_no_transport_at_all_it_says_so_and_names_both_ways_out()
        {
            var result = await Sender(graph: null).SendAsync("someone@example.com", "Subject", "<p>Body</p>", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("no way to send mail", result.Message);
            Assert.Contains("SMTP", result.Message);
        }

        [Fact]
        public async Task An_empty_address_is_refused_before_any_transport_is_chosen()
        {
            var result = await Sender(graph: Complete()).SendAsync("   ", "Subject", "<p>Body</p>", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("No address", result.Message);
        }

        /// <summary>
        /// The TODO the instructions asked for, and the honest answer on a machine where nobody
        /// has configured the app's client secret: Graph says it is not configured rather than
        /// failing somewhere out on the network.
        /// </summary>
        [Fact]
        public async Task Graph_without_a_credential_reports_itself_unconfigured()
        {
            var settings = Settings((SettingsKeys.MailTransport, MailTransports.Graph));
            var result = await new MailSender(settings, null).SendAsync("someone@example.com", "Subject", "<p>Body</p>", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("not configured", result.Message);
            Assert.Contains(PbxEntraSecretSetting, result.Message);
        }

        [Fact]
        public async Task Graph_with_a_credential_but_no_from_address_says_which_is_missing()
        {
            var settings = Settings((SettingsKeys.MailTransport, MailTransports.Graph));
            var result = await new MailSender(settings, Complete()).SendAsync("someone@example.com", "Subject", "<p>Body</p>", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains(SettingsKeys.MailFromAddress, result.Message);
        }

        [Fact]
        public async Task Smtp_without_a_host_says_which_setting_is_missing()
        {
            var settings = Settings((SettingsKeys.MailTransport, MailTransports.Smtp));
            var result = await new MailSender(settings, null).SendAsync("someone@example.com", "Subject", "<p>Body</p>", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains(SettingsKeys.MailSmtpHost, result.Message);
        }

        [Fact]
        public async Task Smtp_with_a_host_but_no_from_address_says_which_setting_is_missing()
        {
            var settings = Settings(
                (SettingsKeys.MailTransport, MailTransports.Smtp),
                (SettingsKeys.MailSmtpHost, "smtp.example.com"));

            var result = await new MailSender(settings, null).SendAsync("someone@example.com", "Subject", "<p>Body</p>", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains(SettingsKeys.MailFromAddress, result.Message);
        }

        /// <summary>An incomplete credential is no credential: a half-filled one must not be tried.</summary>
        [Theory]
        [InlineData("", "client", "secret")]
        [InlineData("tenant", "", "secret")]
        [InlineData("tenant", "client", "")]
        public void A_partly_filled_credential_is_not_complete(string tenant, string client, string secret)
        {
            Assert.False(new GraphCredential(tenant, client, secret).IsComplete);
        }

        [Fact]
        public void A_fully_filled_credential_is_complete()
        {
            Assert.True(Complete().IsComplete);
        }

        private static GraphCredential Complete() => new("a-tenant", "a-client", "a-secret");

        private static MailSender Sender(GraphCredential? graph) => new(Settings(), graph);

        private static MailSettings Settings(params (string Key, string Value)[] stored) =>
            new(stored.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
    }
}
