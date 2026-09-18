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

        /// <summary>
        /// The TCP SIP port. Unset means no TCP transport is rendered at all: UDP is what phones
        /// and providers use here, and a listener nobody asked for is attack surface (D70).
        /// </summary>
        public const string SipTcpPort = "Sip.TcpPort";

        /// <summary>
        /// The TLS SIP port. Stored only: nothing renders a TLS transport yet, because there is no
        /// certificate management, and a pjsip TLS transport without a cert_file fails to load
        /// (D71).
        /// </summary>
        public const string SipTlsPort = "Sip.TlsPort";

        /// <summary>
        /// The STUN server media asks "what does my public address look like?", as host or
        /// host:port. Unset means no STUN, which is the right answer on a server with a public
        /// address of its own (D72).
        /// </summary>
        public const string SipStunServer = "Sip.StunServer";

        /// <summary>
        /// The codecs extensions are offered, comma separated in preference order. Only codecs
        /// whose modules are on the modules.conf allowlist may be named (D73).
        /// </summary>
        public const string SipCodecs = "Sip.Codecs";

        /// <summary>Comma separated CIDRs, e.g. "10.8.20.0/24".</summary>
        public const string SipLocalNets = "Sip.LocalNets";

        /// <summary>Public IP when the server is behind 1:1 NAT. Unset means no NAT.</summary>
        public const string SipExternalAddress = "Sip.ExternalAddress";

        /// <summary>
        /// The username a phone has to present to fetch its provisioning files. It is the user half
        /// of the user:pass in the DHCP option 160 URL, so it is deliberately restricted to
        /// characters that need no escaping in a URL (D77).
        /// </summary>
        public const string ProvisioningUsername = "Provisioning.Username";

        /// <summary>
        /// The matching password, and the second secret in this table. Treat it like
        /// <see cref="AmiSecret"/>: never logged, never rendered into a page, and only ever
        /// compared against what a provisioning request carried (D77).
        /// </summary>
        public const string ProvisioningPassword = "Provisioning.Password";

        /// <summary>
        /// The Polycom web UI's built-in "Polycom" (admin) account password. Polycom fixes the
        /// username on both its device accounts, so only the password is ours to set. Written into
        /// every generated phone config as <c>device.auth.localAdminPassword</c>, and reused as the
        /// credential this app authenticates with when it pushes a config reload or reboot to a
        /// phone over that same web UI (D84).
        /// </summary>
        public const string ProvisioningAdminPassword = "Provisioning.AdminPassword";

        /// <summary>
        /// The Polycom web UI's built-in "User" account password, written into every generated
        /// phone config as <c>device.auth.localUserPassword</c> (D84).
        /// </summary>
        public const string ProvisioningUserPassword = "Provisioning.UserPassword";

        /// <summary>
        /// The NTP server phones are told to set their clock from, written into every phone's
        /// config as <c>tcpIpApp.sntp.address</c>. Defaults to a public pool so most sites need not
        /// touch it, but a site with its own time source can point here instead (D84).
        /// </summary>
        public const string SystemNtpServer = "System.NtpServer";

        /// <summary>
        /// The IANA zone this server's clock is meant to be in, e.g. "Europe/London". A record,
        /// not a lever: Asterisk matches a time condition against its own local time, so what
        /// actually decides open from closed is the machine's timezone. This is written into the
        /// generated dialplan as a comment so the two can be compared (D65).
        /// </summary>
        public const string SystemTimezone = "System.Timezone";

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
            SipTcpPort,
            SipTlsPort,
            SipStunServer,
            SipCodecs,
            SipLocalNets,
            SipExternalAddress,
            ProvisioningUsername,
            ProvisioningPassword,
            ProvisioningAdminPassword,
            ProvisioningUserPassword,
            SystemNtpServer,
            SystemTimezone,
        };

        private static readonly HashSet<string> SecretKeys = new(StringComparer.Ordinal)
        {
            AmiSecret,
            ProvisioningPassword,
            ProvisioningAdminPassword,
            ProvisioningUserPassword,
        };

        public static IReadOnlyCollection<string> All => KnownKeys;

        public static bool IsKnown(string key) => KnownKeys.Contains(key);

        /// <summary>Whether a value is a credential, so callers know not to log or display it.</summary>
        public static bool IsSecret(string key) => SecretKeys.Contains(key);
    }
}
