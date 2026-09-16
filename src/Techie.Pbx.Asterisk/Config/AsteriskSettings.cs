using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Core.Data;

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

            var localNets = Text(settings, SettingsKeys.SipLocalNets);
            if (localNets != null)
            {
                transport.LocalNets = localNets
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            return transport;
        }

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
