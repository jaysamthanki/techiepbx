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
        [InlineData(SettingsKeys.MailTransport)]
        [InlineData(SettingsKeys.MailFromAddress)]
        [InlineData(SettingsKeys.MailFromName)]
        [InlineData(SettingsKeys.MailSmtpHost)]
        [InlineData(SettingsKeys.MailSmtpPort)]
        [InlineData(SettingsKeys.MailSmtpUsername)]
        [InlineData(SettingsKeys.MailSmtpPassword)]
        [InlineData(SettingsKeys.WebRequestLog)]
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

        [Fact]
        public void The_log_directory_has_to_be_an_absolute_path()
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.AsteriskLogDirectory, "/var/log/asterisk"));
            Assert.NotEmpty(SettingsValidation.Errors(SettingsKeys.AsteriskLogDirectory, "var/log/asterisk"));
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
        /// The mail transport is one of two named values (D115). Blank is the third state and is
        /// covered by the blank-is-always-allowed theory above: it means "decide for me".
        /// </summary>
        [Theory]
        [InlineData("graph")]
        [InlineData("smtp")]
        public void A_known_mail_transport_is_accepted(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.MailTransport, value));
        }

        [Theory]
        [InlineData("Graph")]
        [InlineData("sendmail")]
        [InlineData("exchange")]
        public void A_mail_transport_this_system_cannot_send_with_is_rejected(string value)
        {
            Assert.Contains(SettingsValidation.Errors(SettingsKeys.MailTransport, value), e => e.Contains("mail transport must be"));
        }

        /// <summary>
        /// The request log is a toggle (D116), so only the two words a toggle may be. Blank is
        /// covered by the blank-is-always-allowed theory above, and means the default, which is on.
        /// </summary>
        [Theory]
        [InlineData("on")]
        [InlineData("off")]
        public void A_request_log_toggle_is_accepted(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.WebRequestLog, value));
        }

        [Theory]
        [InlineData("true")]
        [InlineData("On")]
        [InlineData("1")]
        [InlineData("yes please")]
        public void Anything_that_is_not_a_toggle_is_rejected_for_the_request_log(string value)
        {
            Assert.Contains(SettingsValidation.Errors(SettingsKeys.WebRequestLog, value), e => e.Contains("request log setting must be"));
        }

        [Theory]
        [InlineData("pbx@example.com")]
        [InlineData("no-reply@example.com")]
        public void A_mail_from_address_that_is_an_address_is_accepted(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.MailFromAddress, value));
        }

        [Theory]
        [InlineData("pbx")]
        [InlineData("pbx at example.com")]
        public void A_mail_from_address_that_is_not_an_address_is_rejected(string value)
        {
            Assert.Contains(SettingsValidation.Errors(SettingsKeys.MailFromAddress, value), e => e.Contains("from address must be"));
        }

        /// <summary>
        /// The from name goes into a mail header, so the characters that would end the header or
        /// start a second one are out — the same reasoning ConfText applies to a conf file.
        /// </summary>
        [Theory]
        [InlineData("Techie PBX\r\nBcc: someone@example.com")]
        [InlineData("Techie \"PBX\"")]
        [InlineData("Techie <pbx@example.com>")]
        public void A_mail_from_name_that_could_forge_a_header_is_rejected(string value)
        {
            Assert.Contains(SettingsValidation.Errors(SettingsKeys.MailFromName, value), e => e.Contains("mail header"));
        }

        [Fact]
        public void An_ordinary_mail_from_name_is_accepted()
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.MailFromName, "Techie PBX"));
        }

        [Theory]
        [InlineData("smtp.sendgrid.net")]
        [InlineData("smtp-relay.gmail.com")]
        [InlineData("10.8.20.9")]
        public void An_smtp_host_that_is_a_host_is_accepted(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.MailSmtpHost, value));
        }

        [Theory]
        [InlineData("smtp://smtp.sendgrid.net")]
        [InlineData("smtp.sendgrid.net:587")]
        [InlineData("smtp.sendgrid.net/submit")]
        public void An_smtp_host_with_a_scheme_or_a_port_on_it_is_rejected(string value)
        {
            Assert.Contains(SettingsValidation.Errors(SettingsKeys.MailSmtpHost, value), e => e.Contains("SMTP host must be"));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("65536")]
        [InlineData("submission")]
        public void An_smtp_port_that_is_not_a_port_is_rejected(string value)
        {
            Assert.Contains(SettingsValidation.Errors(SettingsKeys.MailSmtpPort, value), e => e.Contains("SMTP port must be"));
        }

        /// <summary>
        /// The relay password is a secret like the others, so the table masks it and nothing logs
        /// it (D112). Its only shape rule is the one that would break the protocol: it is whatever
        /// the relay issued — a SendGrid API key, a Google app password.
        /// </summary>
        [Fact]
        public void The_smtp_password_is_a_secret_and_the_username_is_not()
        {
            Assert.True(SettingsKeys.IsSecret(SettingsKeys.MailSmtpPassword));
            Assert.False(SettingsKeys.IsSecret(SettingsKeys.MailSmtpUsername));
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.MailSmtpPassword, "SG.a-long-api-key_with.punctuation"));
            Assert.Contains(SettingsValidation.Errors(SettingsKeys.MailSmtpPassword, "key\r\nQUIT"), e => e.Contains("line breaks"));
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

        /// <summary>
        /// One to nine parking slots (D119). Not ten: a slot is picked up by dialling its number,
        /// so every slot number has to be a single digit.
        /// </summary>
        [Theory]
        [InlineData("1")]
        [InlineData("5")]
        [InlineData("9")]
        public void A_slot_count_of_one_digit_is_accepted(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.ParkingSlots, value));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("10")]
        [InlineData("-1")]
        [InlineData("nine")]
        public void A_slot_count_outside_one_to_nine_is_rejected(string value)
        {
            Assert.NotEmpty(SettingsValidation.Errors(SettingsKeys.ParkingSlots, value));
        }

        [Theory]
        [InlineData("30")]
        [InlineData("60")]
        [InlineData("600")]
        public void A_parking_timeout_in_range_is_accepted(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.ParkingTimeout, value));
        }

        [Theory]
        [InlineData("29")]
        [InlineData("601")]
        [InlineData("")]
        [InlineData("a while")]
        public void A_parking_timeout_out_of_range_is_rejected_unless_it_is_blank(string value)
        {
            // Blank is "not set" everywhere in this table, so it is the one value that is fine.
            var errors = SettingsValidation.Errors(SettingsKeys.ParkingTimeout, value);

            if (value.Length == 0)
                Assert.Empty(errors);
            else
                Assert.NotEmpty(errors);
        }

        /// <summary>A star and one or two digits, which is the whole vocabulary of a feature code.</summary>
        [Theory]
        [InlineData("*3")]
        [InlineData("*72")]
        [InlineData("*0")]
        public void A_park_feature_code_of_a_star_and_digits_is_accepted(string value)
        {
            Assert.Empty(SettingsValidation.Errors(SettingsKeys.ParkingDtmfCode, value));
            Assert.True(SettingsValidation.IsParkingDtmfCode(value));
        }

        [Theory]
        [InlineData("3")]
        [InlineData("#3")]
        [InlineData("*123")]
        [InlineData("*")]
        [InlineData("*3a")]
        [InlineData("*3;evil")]
        public void A_park_feature_code_of_any_other_shape_is_rejected(string value)
        {
            Assert.NotEmpty(SettingsValidation.Errors(SettingsKeys.ParkingDtmfCode, value));
            Assert.False(SettingsValidation.IsParkingDtmfCode(value));
        }

        [Theory]
        [InlineData(SettingsKeys.ParkingEnabled, "on")]
        [InlineData(SettingsKeys.ParkingEnabled, "off")]
        [InlineData(SettingsKeys.ParkingAudio, "silence")]
        [InlineData(SettingsKeys.ParkingAudio, "moh")]
        public void The_parking_word_settings_take_the_words_they_list(string key, string value)
        {
            Assert.Empty(SettingsValidation.Errors(key, value));
        }

        [Theory]
        [InlineData(SettingsKeys.ParkingEnabled, "yes")]
        [InlineData(SettingsKeys.ParkingAudio, "music")]
        [InlineData(SettingsKeys.ParkingAudio, "ringing")]
        public void The_parking_word_settings_refuse_anything_else(string key, string value)
        {
            Assert.NotEmpty(SettingsValidation.Errors(key, value));
        }
    }
}
