namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// Every key the Settings table is allowed to hold. One key/value table instead of typed
    /// columns keeps schema churn down; naming the keys here keeps the table from turning into
    /// a junk drawer, because the repository refuses to write anything not listed.
    /// </summary>
    public static class SettingsKeys
    {
        /// <summary>Where generated conf files are written. Defaults to /etc/asterisk.</summary>
        public const string AsteriskConfDirectory = "Asterisk.ConfDirectory";

        public const string AmiHost = "Ami.Host";
        public const string AmiPort = "Ami.Port";
        public const string AmiUsername = "Ami.Username";

        /// <summary>
        /// The AMI password. The only secret in this table: treat it like Extensions.Secret,
        /// never log it and never render it into anything but manager.conf.
        /// </summary>
        public const string AmiSecret = "Ami.Secret";

        /// <summary>Connect and read timeout in seconds; 0 waits forever.</summary>
        public const string AmiTimeoutSeconds = "Ami.TimeoutSeconds";

        public const string SipBindAddress = "Sip.BindAddress";
        public const string SipPort = "Sip.Port";

        /// <summary>Comma separated CIDRs, e.g. "10.8.20.0/24".</summary>
        public const string SipLocalNets = "Sip.LocalNets";

        /// <summary>Public IP when the server is behind 1:1 NAT. Unset means no NAT.</summary>
        public const string SipExternalAddress = "Sip.ExternalAddress";

        private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
        {
            AsteriskConfDirectory,
            AmiHost,
            AmiPort,
            AmiUsername,
            AmiSecret,
            AmiTimeoutSeconds,
            SipBindAddress,
            SipPort,
            SipLocalNets,
            SipExternalAddress,
        };

        private static readonly HashSet<string> SecretKeys = new(StringComparer.Ordinal)
        {
            AmiSecret,
        };

        public static IReadOnlyCollection<string> All => KnownKeys;

        public static bool IsKnown(string key) => KnownKeys.Contains(key);

        /// <summary>Whether a value is a credential, so callers know not to log or display it.</summary>
        public static bool IsSecret(string key) => SecretKeys.Contains(key);
    }
}
