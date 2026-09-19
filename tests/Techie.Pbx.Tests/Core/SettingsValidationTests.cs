using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The per-key rules the settings page and the repository both ask (D67). Everything here is
    /// a pure function over strings, so there is no database in this file.
    /// </summary>
    public class SettingsValidationTests
    {
        [Fact]
        public void A_known_key_with_a_good_value_has_no_errors()
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.AmiUsername, "tnpbx"));
        }

        [Fact]
        public void An_unknown_key_is_rejected_before_anything_else_is_looked_at()
        {
            var errors = SettingsValidation.Errors("Ami.Scret", "5038");

            Assert.Contains(errors, e => e.Contains("not a known setting"));
        }

        [Fact]
        public void A_value_longer_than_the_limit_is_rejected()
        {
            var errors = SettingsValidation.Errors(SettingsKeys.AmiUsername, new string('x', SettingsValidation.MaxValueLength + 1));

            Assert.Contains(errors, e => e.Contains("characters or fewer"));
        }

        /// <summary>
        /// Blank is how a setting goes back to its default, so no per-key rule may reject it —
        /// otherwise a value could be stored that could never be cleared.
        /// </summary>
        [Theory]
        [InlineData(SettingsKeys.AmiPort)]
        [InlineData(SettingsKeys.SipPort)]
        [InlineData(SettingsKeys.SipTcpPort)]
        [InlineData(SettingsKeys.SipTlsPort)]
        [InlineData(SettingsKeys.SipStunServer)]
        [InlineData(SettingsKeys.SipCodecs)]
        [InlineData(SettingsKeys.SipLocalNets)]
        [InlineData(SettingsKeys.SipExternalAddress)]
        [InlineData(SettingsKeys.SystemTimezone)]
        [InlineData(SettingsKeys.SystemNtpServer)]
        [InlineData(SettingsKeys.ProvisioningAdminPassword)]
        [InlineData(SettingsKeys.ProvisioningUserPassword)]
        public void A_blank_value_is_always_allowed(string key)
        {
            Assert.Empty(SettingsValidation.Errors(key, ""));
            Assert.Empty(SettingsValidation.Errors(key, "   "));
        }

        [Fact]
        public void A_null_value_is_a_missing_value_rather_than_a_blank_one()
        {
            Assert.Contains(SettingsValidation.Errors(SettingsKeys.AmiPort, null), e => e.Contains("required"));
        }

        [Theory]
        [InlineData(SettingsKeys.AmiPort, "5038")]
        [InlineData(SettingsKeys.SipPort, "5060")]
        [InlineData(SettingsKeys.SipTcpPort, "5060")]
        [InlineData(SettingsKeys.SipTlsPort, "5061")]
        [InlineData(SettingsKeys.SipPort, "1")]
        [InlineData(SettingsKeys.SipPort, "65535")]
        public void A_port_is_a_number_in_range(string key, string value)
        {
            Assert.Empty(SettingsValidation.Errors(key, value));
        }

        [Theory]
        [InlineData(SettingsKeys.AmiPort, "0")]
        [InlineData(SettingsKeys.SipPort, "65536")]
        [InlineData(SettingsKeys.SipPort, "-1")]
        [InlineData(SettingsKeys.SipTcpPort, "five thousand")]
        [InlineData(SettingsKeys.SipTcpPort, "5060 ; evil")]
        [InlineData(SettingsKeys.SipTlsPort, "5061.5")]
        public void A_port_that_is_not_a_number_in_range_is_rejected(string key, string value)
        {
            Assert.NotEmpty(SettingsValidation.Errors(key, value));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("600")]
        public void The_ami_timeout_may_be_none_or_ten_minutes(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.AmiTimeoutSeconds, value));
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("601")]
        public void An_ami_timeout_outside_the_range_is_rejected(string value)
        {
            Assert.NotEmpty(SettingsValidation.Errors(SettingsKeys.AmiTimeoutSeconds, value));
        }

        [Theory]
        [InlineData("stun.l.google.com:19302")]
        [InlineData("stun.l.google.com")]
        [InlineData("203.0.113.10")]
        [InlineData("203.0.113.10:3478")]
        public void A_stun_server_is_a_host_with_an_optional_port(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.SipStunServer, value));
            Assert.True(SettingsValidation.IsStunServer(value));
        }

        [Theory]
        [InlineData("stun.l.google.com:0")]
        [InlineData("stun.l.google.com:70000")]
        [InlineData("stun.l.google.com:sip")]
        [InlineData("stun server:3478")]
        [InlineData("stun.l.google.com:19302:19302")]
        [InlineData("-stun.example.com")]
        [InlineData(";evil")]
        public void A_stun_server_that_is_not_a_host_or_whose_port_is_wrong_is_rejected(string value)
        {
            Assert.NotEmpty(SettingsValidation.Errors(SettingsKeys.SipStunServer, value));
            Assert.False(SettingsValidation.IsStunServer(value));
        }

        [Theory]
        [InlineData("ulaw")]
        [InlineData("ulaw,alaw")]
        [InlineData("gsm, ulaw , alaw")]
        public void Codecs_may_name_the_ones_whose_modules_are_loaded(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.SipCodecs, value));
        }

        /// <summary>
        /// Anything whose module is not on the modules.conf allowlist, however real a codec it is
        /// elsewhere (D73).
        /// </summary>
        [Theory]
        [InlineData("opus")]
        [InlineData("g729")]
        [InlineData("ulaw,opus")]
        [InlineData("ULAW")]
        [InlineData(",")]
        public void Codecs_may_not_name_anything_else(string value)
        {
            Assert.NotEmpty(SettingsValidation.Errors(SettingsKeys.SipCodecs, value));
        }

        [Fact]
        public void The_same_codec_twice_is_rejected()
        {
            Assert.Contains(SettingsValidation.Errors(SettingsKeys.SipCodecs, "ulaw,ulaw"), e => e.Contains("only be named once"));
        }

        [Theory]
        [InlineData("10.8.20.0/24")]
        [InlineData("10.8.20.0/24, 192.168.0.0/16")]
        public void Local_networks_are_comma_separated_cidrs(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.SipLocalNets, value));
        }

        /// <summary>
        /// A bare address with no prefix is rejected, because pjsip.conf's local_net takes a
        /// range. Host bits inside the range are not: IPNetwork reads 10.8.20.5/24 as the /24 it
        /// sits in, and the renderer would accept it too — one rule, checked in one way.
        /// </summary>
        [Theory]
        [InlineData("10.8.20.0")]
        [InlineData("10.8.20.0/24, nonsense")]
        [InlineData("10.8.20.0/33")]
        public void A_local_network_that_is_not_a_cidr_is_rejected(string value)
        {
            Assert.NotEmpty(SettingsValidation.Errors(SettingsKeys.SipLocalNets, value));
        }

        [Theory]
        [InlineData(SettingsKeys.SipBindAddress, "0.0.0.0")]
        [InlineData(SettingsKeys.SipExternalAddress, "203.0.113.10")]
        public void An_address_setting_takes_an_ip_address(string key, string value)
        {
            Assert.Empty(SettingsValidation.Errors(key, value));
        }

        [Theory]
        [InlineData(SettingsKeys.SipBindAddress, "every interface")]
        [InlineData(SettingsKeys.SipExternalAddress, "pbx.example.com")]
        public void An_address_setting_refuses_anything_that_is_not_one(string key, string value)
        {
            Assert.NotEmpty(SettingsValidation.Errors(key, value));
        }

        [Fact]
        public void The_conf_directory_has_to_be_an_absolute_path()
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.AsteriskConfDirectory, "/etc/asterisk"));
            Assert.NotEmpty(SettingsValidation.Errors(SettingsKeys.AsteriskConfDirectory, "etc/asterisk"));
        }

        [Theory]
        [InlineData("Europe/London")]
        [InlineData("America/Los_Angeles")]
        [InlineData("Etc/UTC")]
        public void A_real_zone_name_is_accepted(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.SystemTimezone, value));
        }

        [Theory]
        [InlineData("Europe London")]
        [InlineData("Europe/London; evil")]
        [InlineData("Europe/London\nHere")]
        public void A_zone_name_of_the_wrong_shape_is_rejected(string value)
        {
            Assert.Contains(SettingsValidation.Errors(SettingsKeys.SystemTimezone, value), e => e.Contains("IANA zone name"));
        }

        [Theory]
        [InlineData("phones")]
        [InlineData("tnpbx-phones")]
        [InlineData("prov.user_1")]
        public void A_provisioning_username_of_url_safe_characters_is_accepted(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.ProvisioningUsername, value));
        }

        /// <summary>
        /// The username is the user half of the DHCP option 160 URL, so a character that would have
        /// to be escaped in one is refused rather than quietly stored (D77).
        /// </summary>
        [Theory]
        [InlineData("phones:extra")]
        [InlineData("phones@example")]
        [InlineData("phones/admin")]
        [InlineData("two words")]
        public void A_provisioning_username_that_would_break_the_url_is_rejected(string value)
        {
            Assert.Contains(SettingsValidation.Errors(SettingsKeys.ProvisioningUsername, value), e => e.Contains("option 160"));
        }

        [Theory]
        [InlineData("s3cret-pass")]
        [InlineData("aaaabbbb")]
        [InlineData("A.long_one~with-everything.0123456789")]
        public void A_provisioning_password_of_url_safe_characters_is_accepted(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.ProvisioningPassword, value));
        }

        [Theory]
        [InlineData("short12")]
        [InlineData("has:colon1")]
        [InlineData("has@at123")]
        [InlineData("has space1")]
        public void A_provisioning_password_that_is_too_short_or_would_break_the_url_is_rejected(string value)
        {
            Assert.Contains(SettingsValidation.Errors(SettingsKeys.ProvisioningPassword, value), e => e.Contains("option 160"));
        }

        /// <summary>
        /// The provisioning password is the second secret in this table, so nothing may log or
        /// render it (D77).
        /// </summary>
        [Fact]
        public void The_provisioning_password_is_a_secret_and_the_username_is_not()
        {
            Assert.True(SettingsKeys.IsSecret(SettingsKeys.ProvisioningPassword));
            Assert.False(SettingsKeys.IsSecret(SettingsKeys.ProvisioningUsername));
            Assert.True(SettingsKeys.IsKnown(SettingsKeys.ProvisioningUsername));
            Assert.True(SettingsKeys.IsKnown(SettingsKeys.ProvisioningPassword));
        }

        /// <summary>
        /// The system's own zone database is the list of zones. On a machine with no tzdata there
        /// is no list to check against, and the name is accepted on its shape alone.
        /// </summary>
        [Fact]
        public void A_zone_name_that_this_server_does_not_have_is_rejected()
        {
            var errors = SettingsValidation.Errors(SettingsKeys.SystemTimezone, "Europe/Nowhere");

            if (SettingsValidation.ZoneDatabaseIsReadable())
                Assert.Contains(errors, e => e.Contains("no timezone called"));
            else
                Assert.Empty(errors);
        }

        [Theory]
        [InlineData("pool.ntp.org")]
        [InlineData("ntp.example.com")]
        [InlineData("10.8.20.1")]
        public void An_ntp_server_that_is_a_hostname_or_ip_is_accepted(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.SystemNtpServer, value));
        }

        [Theory]
        [InlineData("pool ntp org")]
        [InlineData("-bad.example.com")]
        [InlineData(";evil")]
        public void An_ntp_server_that_is_not_a_host_is_rejected(string value)
        {
            Assert.NotEmpty(SettingsValidation.Errors(SettingsKeys.SystemNtpServer, value));
        }

        /// <summary>
        /// The hostname phones are told to reach the PBX on (D105): a bare hostname or IP, or
        /// empty — empty is the default and means "whatever host the phone asked on".
        /// </summary>
        [Theory]
        [InlineData("pbx.example.com")]
        [InlineData("10.8.20.8")]
        [InlineData("")]
        public void A_hostname_that_is_bare_or_empty_is_accepted(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.SystemHostname, value));
        }

        [Theory]
        [InlineData("http://pbx.example.com")]
        [InlineData("pbx.example.com/polycom")]
        [InlineData("pbx techie")]
        [InlineData(";evil")]
        public void A_hostname_that_is_not_bare_is_rejected(string value)
        {
            Assert.NotEmpty(SettingsValidation.Errors(SettingsKeys.SystemHostname, value));
        }

        /// <summary>
        /// The two Polycom device account passwords are the same shape as the provisioning
        /// password (D84) even though they never end up in a URL, because that shape is also safe
        /// to write straight into an XML attribute.
        /// </summary>
        [Theory]
        [InlineData(SettingsKeys.ProvisioningAdminPassword)]
        [InlineData(SettingsKeys.ProvisioningUserPassword)]
        public void A_device_password_of_url_safe_characters_is_accepted(string key)
        {
            Assert.Empty(SettingsValidation.Errors(key, "AAAAbbbb1111"));
        }

        [Theory]
        [InlineData(SettingsKeys.ProvisioningAdminPassword)]
        [InlineData(SettingsKeys.ProvisioningUserPassword)]
        public void A_device_password_that_is_too_short_or_has_an_odd_character_is_rejected(string key)
        {
            Assert.NotEmpty(SettingsValidation.Errors(key, "short1"));
            Assert.NotEmpty(SettingsValidation.Errors(key, "has a space in it"));
        }

        /// <summary>Both device passwords are secrets: they authenticate against the phone's own web UI.</summary>
        [Fact]
        public void Both_device_passwords_are_secrets()
        {
            Assert.True(SettingsKeys.IsSecret(SettingsKeys.ProvisioningAdminPassword));
            Assert.True(SettingsKeys.IsSecret(SettingsKeys.ProvisioningUserPassword));
        }
    }
}
