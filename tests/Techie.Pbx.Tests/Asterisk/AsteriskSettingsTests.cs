using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Turning Settings rows into the objects the AMI client and the renderers take.
    /// </summary>
    public class AsteriskSettingsTests
    {
        private static Dictionary<string, string> Values(params (string Key, string Value)[] pairs) =>
            pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        [Fact]
        public void An_empty_settings_table_gives_the_built_in_defaults()
        {
            var settings = Values();

            var ami = AsteriskSettings.Ami(settings);
            var transport = AsteriskSettings.Transport(settings);

            Assert.Equal(AsteriskSettings.DefaultConfDirectory, AsteriskSettings.ConfDirectory(settings));
            Assert.Equal("127.0.0.1", ami.Host);
            Assert.Equal(5038, ami.Port);
            Assert.Equal(10, ami.TimeoutSeconds);
            Assert.Equal("0.0.0.0", transport.BindAddress);
            Assert.Equal(5060, transport.Port);
            Assert.Empty(transport.LocalNets);
            Assert.Null(transport.ExternalAddress);
        }

        [Fact]
        public void Ami_settings_come_out_of_the_database_secret_included()
        {
            var ami = AsteriskSettings.Ami(Values(
                (SettingsKeys.AmiHost, "10.8.20.50"),
                (SettingsKeys.AmiPort, "5039"),
                (SettingsKeys.AmiUsername, "tnpbx"),
                (SettingsKeys.AmiSecret, "not-a-real-secret"),
                (SettingsKeys.AmiTimeoutSeconds, "0")));

            Assert.Equal("10.8.20.50", ami.Host);
            Assert.Equal(5039, ami.Port);
            Assert.Equal("tnpbx", ami.Username);
            Assert.Equal("not-a-real-secret", ami.Secret);
            Assert.Equal(0, ami.TimeoutSeconds);
            Assert.Empty(ami.Validate());
        }

        [Fact]
        public void Transport_settings_include_the_nat_values()
        {
            var transport = AsteriskSettings.Transport(Values(
                (SettingsKeys.SipBindAddress, "0.0.0.0"),
                (SettingsKeys.SipPort, "5060"),
                (SettingsKeys.SipLocalNets, "10.8.20.0/24, 192.168.0.0/16"),
                (SettingsKeys.SipExternalAddress, "203.0.113.10")));

            Assert.Equal(new[] { "10.8.20.0/24", "192.168.0.0/16" }, transport.LocalNets);
            Assert.Equal("203.0.113.10", transport.ExternalAddress);
            Assert.Empty(transport.Validate());
        }

        [Fact]
        public void A_blank_value_counts_as_unset()
        {
            var settings = Values(
                (SettingsKeys.AsteriskConfDirectory, "  "),
                (SettingsKeys.SipExternalAddress, ""),
                (SettingsKeys.SipLocalNets, " "));

            Assert.Equal(AsteriskSettings.DefaultConfDirectory, AsteriskSettings.ConfDirectory(settings));
            Assert.Null(AsteriskSettings.Transport(settings).ExternalAddress);
            Assert.Empty(AsteriskSettings.Transport(settings).LocalNets);
        }

        [Fact]
        public void A_value_that_is_not_a_number_falls_back_to_the_default()
        {
            // Validation on the settings object is what reports bad values; loading never throws.
            Assert.Equal(5038, AsteriskSettings.Ami(Values((SettingsKeys.AmiPort, "five thousand"))).Port);
        }

        [Fact]
        public void Missing_ami_credentials_are_reported_by_validation_not_by_the_loader()
        {
            var ami = AsteriskSettings.Ami(Values((SettingsKeys.AmiHost, "127.0.0.1")));

            Assert.Equal(2, ami.Validate().Count);
        }
    }
}
