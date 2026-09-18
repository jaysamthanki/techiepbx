using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Builds the settings objects from the rows of the Settings table. Pure functions over an
    /// already loaded key/value map, so AmiSettings and PjsipTransport stay ignorant of storage.
    /// A missing or blank value falls back to the default on the settings object itself.
    /// </summary>
    public static class AsteriskSettings
    {
        /// <summary>Where a normal Debian install keeps Asterisk's config.</summary>
        public const string DefaultConfDirectory = "/etc/asterisk";

        /// <summary>
        /// What a fresh Debian server's clock is set to until somebody says otherwise, which is
        /// also the honest thing to write into the dialplan when nobody has (D65).
        /// </summary>
        public const string DefaultTimezone = "Etc/UTC";

        public static string ConfDirectory(IReadOnlyDictionary<string, string> settings) =>
            Text(settings, SettingsKeys.AsteriskConfDirectory) ?? DefaultConfDirectory;

        public static AmiSettings Ami(IReadOnlyDictionary<string, string> settings)
        {
            var ami = new AmiSettings();

            ami.Host = Text(settings, SettingsKeys.AmiHost) ?? ami.Host;
            ami.Port = Number(settings, SettingsKeys.AmiPort, ami.Port);
            ami.Username = Text(settings, SettingsKeys.AmiUsername) ?? ami.Username;
            ami.Secret = Text(settings, SettingsKeys.AmiSecret) ?? ami.Secret;
            ami.TimeoutSeconds = Number(settings, SettingsKeys.AmiTimeoutSeconds, ami.TimeoutSeconds);

            return ami;
        }

        public static PjsipTransport Transport(IReadOnlyDictionary<string, string> settings)
        {
            var transport = new PjsipTransport();

            transport.BindAddress = Text(settings, SettingsKeys.SipBindAddress) ?? transport.BindAddress;
            transport.Port = Number(settings, SettingsKeys.SipPort, transport.Port);
            transport.ExternalAddress = Text(settings, SettingsKeys.SipExternalAddress);
            transport.StunServer = Text(settings, SettingsKeys.SipStunServer);

            // Unset means off, not a default: no port, no TCP transport in the file at all (D70).
            // Sip.TlsPort is deliberately not read — nothing renders a TLS transport yet (D71).
            var tcpPort = Text(settings, SettingsKeys.SipTcpPort);
            transport.TcpPort = int.TryParse(tcpPort, out var tcp) ? tcp : null;

            var codecs = Text(settings, SettingsKeys.SipCodecs);
            if (codecs != null)
                transport.Codecs = SipCodecs.Parse(codecs);

            var localNets = Text(settings, SettingsKeys.SipLocalNets);
            if (localNets != null)
            {
                transport.LocalNets = localNets
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            return transport;
        }

        /// <summary>
        /// The IANA zone recorded for this server, e.g. "Europe/London". Anything that is not
        /// shaped like a zone name falls back to the default rather than throwing, for the same
        /// reason a non-numeric port does — and because this one is written into a conf file, where
        /// a stray character would be an apply that fails over a comment (D65).
        /// </summary>
        public static string Timezone(IReadOnlyDictionary<string, string> settings)
        {
            var value = Text(settings, SettingsKeys.SystemTimezone);

            return value != null && IsZoneName(value) ? value : DefaultTimezone;
        }

        /// <summary>
        /// Letters, digits and the few punctuation marks a zone name is made of. The rule itself
        /// lives with the other settings rules in <see cref="SettingsValidation"/>, so the check
        /// that decides what gets written into a conf file and the check the settings page makes
        /// cannot drift apart.
        /// </summary>
        public static bool IsZoneName(string value) => SettingsValidation.IsZoneName(value);

        /// <summary>A blank value counts as not set, so clearing a field in the UI works.</summary>
        private static string? Text(IReadOnlyDictionary<string, string> settings, string key) =>
            settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

        /// <summary>
        /// A non-numeric value falls back to the default rather than throwing: validation on the
        /// settings objects is what reports bad values to the user.
        /// </summary>
        private static int Number(IReadOnlyDictionary<string, string> settings, string key, int fallback) =>
            int.TryParse(Text(settings, key), out var value) ? value : fallback;
    }
}
