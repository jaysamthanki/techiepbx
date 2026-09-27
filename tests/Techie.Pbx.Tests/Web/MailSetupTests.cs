using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Mail;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Pages.Settings;

namespace Techie.Pbx.Tests.Web
{
    /// <summary>
    /// The Email tab's mail setups: which one the stored settings look like, and what each one
    /// offers to fill in. The guess decides what an admin sees first, so it has to follow what a
    /// send would actually do rather than whatever happens to be typed in.
    /// </summary>
    public class MailSetupTests
    {
        private static string Guess(string transport, params (string Key, string Value)[] values)
        {
            var settings = new MailSettings(values.ToDictionary(pair => pair.Key, pair => pair.Value));
            return MailSetup.Guess(settings, transport);
        }

        [Fact]
        public void Nothing_set_up_picks_nothing()
        {
            Assert.Equal("", Guess(MailSettings.NoTransport));
        }

        [Fact]
        public void Graph_in_force_is_graph_even_with_a_relay_for_voicemail()
        {
            Assert.Equal(MailSetup.GraphKey,
                Guess(MailTransports.Graph, (SettingsKeys.MailSmtpHost, "smtp.sendgrid.net")));
        }

        [Theory]
        [InlineData("smtp.sendgrid.net", MailSetup.SendGridKey)]
        [InlineData("SMTP.SendGrid.net", MailSetup.SendGridKey)]
        [InlineData("smtp.gmail.com", MailSetup.GoogleKey)]
        [InlineData("smtp-relay.gmail.com", MailSetup.GoogleKey)]
        [InlineData("mail.example.com", MailSetup.SmtpKey)]
        [InlineData("", MailSetup.SmtpKey)]
        public void Smtp_is_told_apart_by_its_host(string host, string expected)
        {
            Assert.Equal(expected, Guess(MailTransports.Smtp,
                (SettingsKeys.MailTransport, MailTransports.Smtp), (SettingsKeys.MailSmtpHost, host)));
        }

        [Fact]
        public void A_relay_with_no_transport_still_shows_the_relay()
        {
            // Blank transport and no Graph credential means no mail at all, but the admin has
            // clearly started on SendGrid, so that is the setup to show them.
            Assert.Equal(MailSetup.SendGridKey,
                Guess(MailSettings.NoTransport, (SettingsKeys.MailSmtpHost, "smtp.sendgrid.net")));
        }

        [Fact]
        public void Every_setup_is_found_by_its_key_and_nothing_else_is()
        {
            foreach (var setup in MailSetup.All)
                Assert.Same(setup, MailSetup.For(setup.Key));

            Assert.Null(MailSetup.For(""));
            Assert.Null(MailSetup.For(null));
            Assert.Null(MailSetup.For("postfix"));
        }

        [Fact]
        public void Every_setup_shows_only_known_mail_keys()
        {
            foreach (var setup in MailSetup.All)
            {
                var keys = setup.Sections(new Dictionary<string, string>()).SelectMany(section => section.Rows).Select(row => row.Key);

                Assert.All(keys, key =>
                {
                    Assert.True(SettingsKeys.IsKnown(key));
                    Assert.StartsWith("Mail.", key);
                });
            }
        }

        [Fact]
        public void Sendgrid_suggests_its_host_and_the_apikey_username()
        {
            var sendGrid = MailSetup.For(MailSetup.SendGridKey)!;

            Assert.Equal(MailTransports.Smtp, sendGrid.Suggestion(SettingsKeys.MailTransport));
            Assert.Equal("smtp.sendgrid.net", sendGrid.Suggestion(SettingsKeys.MailSmtpHost));
            Assert.Equal("apikey", sendGrid.Suggestion(SettingsKeys.MailSmtpUsername));
            Assert.Equal("", sendGrid.Suggestion(SettingsKeys.MailSmtpPassword));
        }

        [Fact]
        public void Suggested_transports_are_ones_the_form_can_store()
        {
            foreach (var setup in MailSetup.All)
            {
                var transport = setup.Suggestion(SettingsKeys.MailTransport);

                Assert.True(transport.Length == 0 || MailTransports.IsKnown(transport));
                Assert.Equal(setup.Transport, transport);
            }
        }

        [Fact]
        public void Only_setups_with_suggestions_pass_themselves_to_the_edit_form()
        {
            var advanced = MailSetup.For(MailSetup.AdvancedKey)!;
            var graph = MailSetup.For(MailSetup.GraphKey)!;

            Assert.All(advanced.Sections(new Dictionary<string, string>()), section => Assert.Equal("", section.Setup));
            Assert.All(graph.Sections(new Dictionary<string, string>()), section => Assert.Equal(MailSetup.GraphKey, section.Setup));
        }
    }
}
