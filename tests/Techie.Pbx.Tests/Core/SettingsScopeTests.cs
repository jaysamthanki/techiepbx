using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// What each setting reaches, which is what decides whether saving it lights the apply button
    /// (D103). The classification is only worth anything if it matches the code that actually
    /// reads each key, so these tests name the keys rather than trusting the prefix they start
    /// with — <c>System.NtpServer</c> and <c>System.Timezone</c> share a prefix and are in
    /// different scopes, and so do <c>Ami.Port</c> and <c>Ami.TimeoutSeconds</c>.
    /// </summary>
    public class SettingsScopeTests
    {
        /// <summary>
        /// The fallback in <see cref="SettingsKeys.ScopeOf"/> means a forgotten key still behaves
        /// safely, but it should never be reached: a new key gets a scope with it.
        /// </summary>
        [Fact]
        public void Every_known_key_is_classified()
        {
            var unclassified = SettingsKeys.All.Where(key => !SettingsKeys.AllScopes.ContainsKey(key)).ToList();

            Assert.Empty(unclassified);
            Assert.Equal(SettingsKeys.All.Count, SettingsKeys.AllScopes.Count);
        }

        /// <summary>Nothing may be classified that is not a key in the first place.</summary>
        [Fact]
        public void Nothing_is_classified_that_is_not_a_key()
        {
            Assert.All(SettingsKeys.AllScopes.Keys, key => Assert.True(SettingsKeys.IsKnown(key), key));
        }

        /// <summary>
        /// The Asterisk set is exactly the keys something in ConfigApplier.Render reads: the conf
        /// directory it writes to, the AMI account manager.conf carries, every Sip key that
        /// AsteriskSettings.Transport turns into pjsip.conf or rtp.conf, the timezone every
        /// GotoIfTime in the generated dialplan names (D74), and every Parking key — those land in
        /// features.conf, res_parking.conf and the dialplan's slot entries, so all six need an
        /// apply (D119, D122).
        /// </summary>
        [Fact]
        public void The_asterisk_scope_is_the_keys_a_generated_conf_file_carries()
        {
            Assert.Equal(
                new[]
                {
                    SettingsKeys.AmiHost,
                    SettingsKeys.AmiPort,
                    SettingsKeys.AmiSecret,
                    SettingsKeys.AmiUsername,
                    SettingsKeys.AsteriskConfDirectory,
                    SettingsKeys.ParkingAudio,
                    SettingsKeys.ParkingDtmfCode,
                    SettingsKeys.ParkingEnabled,
                    SettingsKeys.ParkingMusicClass,
                    SettingsKeys.ParkingSlots,
                    SettingsKeys.ParkingTimeout,
                    SettingsKeys.SipBindAddress,
                    SettingsKeys.SipCodecs,
                    SettingsKeys.SipExternalAddress,
                    SettingsKeys.SipLocalNets,
                    SettingsKeys.SipMaxContacts,
                    SettingsKeys.SipPort,
                    SettingsKeys.SipStunServer,
                    SettingsKeys.SipTcpPort,
                    SettingsKeys.SipTlsPort,
                    SettingsKeys.SystemTimezone,
                },
                KeysIn(SettingScope.Asterisk));
        }

        /// <summary>
        /// The provisioning gate, the two Polycom device passwords and the NTP server are read by
        /// the provisioning controllers and nothing else. Those files are generated per request
        /// (D79), so a change is already live and there is nothing to apply.
        /// </summary>
        [Fact]
        public void The_phones_scope_is_the_keys_only_a_phone_config_carries()
        {
            Assert.Equal(
                new[]
                {
                    SettingsKeys.ProvisioningAdminPassword,
                    SettingsKeys.ProvisioningPassword,
                    SettingsKeys.ProvisioningUserPassword,
                    SettingsKeys.ProvisioningUsername,
                    SettingsKeys.SystemHostname,
                    SettingsKeys.SystemNtpServer,
                },
                KeysIn(SettingScope.Phones));
        }

        /// <summary>
        /// What only this application reads: how long our AMI client waits, the directory the
        /// Logs page reads Asterisk's own logs from, the three ACME ordering details, and the mail
        /// settings. The certificate an order produces is a row in Certificates, and that
        /// repository raises the marker itself, so the file Asterisk reads is still applied (D101).
        ///
        /// The Mail keys are here and not in the Asterisk scope on purpose (D115): this app sends
        /// its own mail, and voicemail-to-email remains Asterisk's own job through a local MTA —
        /// <c>VoicemailConfRenderer</c> still writes no <c>serveremail</c>, so no generated conf
        /// file carries any of these and there is nothing an apply would write.
        ///
        /// <c>Web.RequestLog</c> is here too (D116): it switches on Kestrel's own request log, which
        /// nothing outside this process reads. It is the one App key that does not take effect
        /// straight away — the logger is middleware, so it is read when the service starts — and
        /// the scope is still App, because the alternative would be lighting the apply button for
        /// an apply that writes no file.
        /// </summary>
        [Fact]
        public void The_app_scope_is_the_keys_nothing_outside_this_process_reads()
        {
            Assert.Equal(
                new[]
                {
                    SettingsKeys.AmiTimeoutSeconds,
                    SettingsKeys.AsteriskLogDirectory,
                    SettingsKeys.CertAcmeAccountKeyPem,
                    SettingsKeys.CertAcmeServer,
                    SettingsKeys.CertEmail,
                    SettingsKeys.MailFromAddress,
                    SettingsKeys.MailFromName,
                    SettingsKeys.MailSmtpHost,
                    SettingsKeys.MailSmtpPassword,
                    SettingsKeys.MailSmtpPort,
                    SettingsKeys.MailSmtpUsername,
                    SettingsKeys.MailTransport,
                    SettingsKeys.WebRequestLog,
                },
                KeysIn(SettingScope.App));
        }

        /// <summary>
        /// The examples worth spelling out, because they are the ones a prefix would get wrong.
        /// </summary>
        [Theory]
        [InlineData(SettingsKeys.ProvisioningUsername, SettingScope.Phones)]
        [InlineData(SettingsKeys.ProvisioningPassword, SettingScope.Phones)]
        [InlineData(SettingsKeys.ProvisioningAdminPassword, SettingScope.Phones)]
        [InlineData(SettingsKeys.ProvisioningUserPassword, SettingScope.Phones)]
        [InlineData(SettingsKeys.SystemNtpServer, SettingScope.Phones)]
        [InlineData(SettingsKeys.SystemHostname, SettingScope.Phones)]
        [InlineData(SettingsKeys.SystemTimezone, SettingScope.Asterisk)]
        [InlineData(SettingsKeys.SipCodecs, SettingScope.Asterisk)]
        [InlineData(SettingsKeys.SipTlsPort, SettingScope.Asterisk)]
        [InlineData(SettingsKeys.AmiSecret, SettingScope.Asterisk)]
        [InlineData(SettingsKeys.AmiTimeoutSeconds, SettingScope.App)]
        [InlineData(SettingsKeys.AsteriskLogDirectory, SettingScope.App)]
        [InlineData(SettingsKeys.CertEmail, SettingScope.App)]
        [InlineData(SettingsKeys.WebRequestLog, SettingScope.App)]
        [InlineData(SettingsKeys.ParkingEnabled, SettingScope.Asterisk)]
        [InlineData(SettingsKeys.ParkingAudio, SettingScope.Asterisk)]
        public void Keys_are_scoped_by_what_reads_them(string key, SettingScope expected)
        {
            Assert.Equal(expected, SettingsKeys.ScopeOf(key));
        }

        /// <summary>
        /// A key that is not in the map at all — a new one somebody forgot — counts as Asterisk:
        /// an apply that writes nothing costs a click, where a missed one leaves Asterisk running
        /// config nobody was told had changed.
        /// </summary>
        [Fact]
        public void An_unclassified_key_falls_back_to_asterisk()
        {
            Assert.Equal(SettingScope.Asterisk, SettingsKeys.ScopeOf("Something.Nobody.Listed"));
        }

        private static List<string> KeysIn(SettingScope scope) => SettingsKeys.AllScopes
            .Where(entry => entry.Value == scope)
            .Select(entry => entry.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
    }
}
