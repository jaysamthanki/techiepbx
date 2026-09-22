using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// Every key the Settings table is allowed to hold. One key/value table instead of typed
    /// columns keeps schema churn down; naming the keys here keeps the table from turning into
    /// a junk drawer, because the repository refuses to write anything not listed.
    ///
    /// Each key also carries a <see cref="SettingScope"/>: what reads it, and so whether changing
    /// it is a config change at all (D103).
    /// </summary>
    public static class SettingsKeys
    {
        /// <summary>Where generated conf files are written. Defaults to /etc/asterisk.</summary>
        public const string AsteriskConfDirectory = "Asterisk.ConfDirectory";

        /// <summary>
        /// Where Asterisk writes its logs. Defaults to <see cref="Diagnostics.LogTail.DefaultLogDirectory"/>
        /// — asterisk.conf leaves directories at their built-in defaults, so this is only different
        /// if a site has moved them. Nothing generated reads this back (D103): it is the directory
        /// the Logs page reads from, not one Asterisk is told to use.
        /// </summary>
        public const string AsteriskLogDirectory = "Asterisk.LogDirectory";

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

        /// <summary>
        /// How many devices may register to one extension at once, for every extension (D110).
        /// An office phone and a laptop softphone on the same number is 2; the default 1 means
        /// a second registration replaces the first.
        /// </summary>
        public const string SipMaxContacts = "Sip.MaxContacts";

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
        /// every generated Polycom config as <c>device.auth.localAdminPassword</c> and every Yealink
        /// one as <c>security.user_password = admin:...</c> (D133), and reused as the
        /// credential this app authenticates with when it pushes a config reload or reboot to a
        /// phone over that same web UI (D84).
        /// </summary>
        public const string ProvisioningAdminPassword = "Provisioning.AdminPassword";

        /// <summary>
        /// The Polycom web UI's built-in "User" account password, written into every generated
        /// Polycom config as <c>device.auth.localUserPassword</c> (D84) and every Yealink one as
        /// <c>security.user_password = user:...</c> (D133).
        /// </summary>
        public const string ProvisioningUserPassword = "Provisioning.UserPassword";

        /// <summary>
        /// The NTP server phones are told to set their clock from, written into every phone's
        /// config as <c>tcpIpApp.sntp.address</c>. Defaults to a public pool so most sites need not
        /// touch it, but a site with its own time source can point here instead (D84).
        /// </summary>
        public const string SystemNtpServer = "System.NtpServer";

        /// <summary>
        /// The hostname phones are told to reach the PBX on — the SIP server address and the
        /// provisioning URL in every generated phone config (D105). Empty means "use whatever
        /// host the phone just asked us on", which keeps working when the box is reachable by
        /// address only; set it when the site has a real name, so a phone that first contacts
        /// the box by IP still ends up on the name.
        /// </summary>
        public const string SystemHostname = "System.Hostname";

        /// <summary>
        /// The ACME directory the certificate service orders from (D97). Defaults to Let's
        /// Encrypt's production endpoint; the staging one is the other value it accepts, for
        /// testing an order without spending a rate limit on a real certificate.
        /// </summary>
        public const string CertAcmeServer = "Cert.AcmeServer";

        /// <summary>
        /// The ACME account key, PEM encoded, generated the first time a certificate is ordered and
        /// then reused for every order and renewal after it (D98). A secret: it is the credential
        /// that proves this server is the account holder, so it is treated exactly like
        /// <see cref="AmiSecret"/> — never logged, never rendered into a page.
        /// </summary>
        public const string CertAcmeAccountKeyPem = "Cert.AcmeAccountKeyPem";

        /// <summary>
        /// The contact address the ACME account is registered with. Let's Encrypt emails it when a
        /// certificate is about to expire and nothing has renewed it, which is the backstop behind
        /// our own daily renewal (D100).
        /// </summary>
        public const string CertEmail = "Cert.Email";

        /// <summary>
        /// The IANA zone this server's clock is meant to be in, e.g. "Europe/London". A record,
        /// not a lever: Asterisk matches a time condition against its own local time, so what
        /// actually decides open from closed is the machine's timezone. This is written into the
        /// generated dialplan as a comment so the two can be compared (D65).
        /// </summary>
        public const string SystemTimezone = "System.Timezone";

        /// <summary>
        /// How mail leaves this box: a <see cref="Models.MailTransports"/> value, or blank for
        /// "decide for me" — Graph where the app has an Entra app credential to send with, and no
        /// mail at all where it has not (D115).
        /// </summary>
        public const string MailTransport = "Mail.Transport";

        /// <summary>
        /// The address mail is sent from. Graph sends as this mailbox, so on Graph it has to be a
        /// real mailbox in the tenant (a shared one is the usual choice) rather than any address
        /// the admin fancies (D115).
        /// </summary>
        public const string MailFromAddress = "Mail.FromAddress";

        /// <summary>The display name beside <see cref="MailFromAddress"/>, e.g. "Techie PBX".</summary>
        public const string MailFromName = "Mail.FromName";

        /// <summary>The submission host, when the transport is SMTP. e.g. smtp.sendgrid.net.</summary>
        public const string MailSmtpHost = "Mail.Smtp.Host";

        /// <summary>The submission port. Defaults to 587, the submission port with STARTTLS.</summary>
        public const string MailSmtpPort = "Mail.Smtp.Port";

        /// <summary>
        /// The SMTP username. Blank means submit without authenticating, which only an internal
        /// relay that trusts this host by address will accept.
        /// </summary>
        public const string MailSmtpUsername = "Mail.Smtp.Username";

        /// <summary>
        /// The SMTP password, and a secret: treat it exactly like <see cref="AmiSecret"/> — never
        /// logged, and shown only in the edit form an admin already signed in to open (D112).
        /// </summary>
        public const string MailSmtpPassword = "Mail.Smtp.Password";

        /// <summary>
        /// The shared secret the <c>voicemail-mail</c> script proves itself with when it calls this
        /// application back to have a voicemail email composed and sent (D129). A secret: treat it
        /// exactly like <see cref="AmiSecret"/> — never logged, and shown only in the edit form an
        /// admin already signed in to open (D112).
        ///
        /// Generated on the first start that finds it missing, and written into
        /// <c>Config/mail.json</c> beside the relay details, because that file is the whole
        /// interface between this application and that script. It is the <b>only</b> thing
        /// protecting the endpoint, so clearing it does not open the endpoint — it closes it, and
        /// voicemail email falls back to the script relaying what app_voicemail composed.
        /// </summary>
        public const string MailVoicemailCallbackToken = "Mail.VoicemailCallbackToken";

        /// <summary>
        /// Whether Kestrel writes the W3C request log — one line per web request, with the client
        /// address, what it asked for and what it got (D116). A <see cref="Models.Toggles"/> value,
        /// read once at startup: the logger is built into the request pipeline or left out of it,
        /// so a change to this needs the service restarted.
        /// </summary>
        public const string WebRequestLog = "Web.RequestLog";

        /// <summary>
        /// What a parked caller hears: a <see cref="Models.ParkingAudio"/> value, silence or the
        /// uploaded music on hold (D119). Written into res_parking.conf as parkedmusicclass, or
        /// deliberately left out of it, which is what makes silence silence.
        /// </summary>
        public const string ParkingAudio = "Parking.Audio";

        /// <summary>
        /// The DTMF a user presses mid-call to park it: a star and one or two digits, default
        /// <c>*3</c> (D119). It is the <c>parkcall</c> entry of the generated features.conf, so it
        /// is matched inside a live call rather than dialled, and cannot collide with an extension.
        /// </summary>
        public const string ParkingDtmfCode = "Parking.DtmfCode";

        /// <summary>
        /// Whether call parking is generated at all: a <see cref="Models.Toggles"/> value, off
        /// until somebody turns it on (D119). Off means no park feature code, no slot routes in
        /// the dialplan and an empty res_parking.conf — the modules stay loaded, but nothing can
        /// reach them.
        /// </summary>
        public const string ParkingEnabled = "Parking.Enabled";

        /// <summary>
        /// Which music on hold class a parked caller hears, by name, when <see cref="ParkingAudio"/>
        /// says music (D122). Written into res_parking.conf as parkedmusicclass; the classes
        /// themselves are the Music on hold page's.
        /// </summary>
        public const string ParkingMusicClass = "Parking.MusicClass";

        /// <summary>
        /// How many parking slots there are, 1 to 9, default 9 (D119). One digit each, because a
        /// slot is retrieved by dialling its number and extensions here are three digits or more.
        /// </summary>
        public const string ParkingSlots = "Parking.Slots";

        /// <summary>
        /// How long a call stays parked before it comes back to the phone that parked it, in
        /// seconds: 30 to 600, default 60 (D119). res_parking.conf's parkingtime.
        /// </summary>
        public const string ParkingTimeout = "Parking.Timeout";

        private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
        {
            AsteriskConfDirectory,
            AsteriskLogDirectory,
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
            SipMaxContacts,
            SipExternalAddress,
            ProvisioningUsername,
            ProvisioningPassword,
            ProvisioningAdminPassword,
            ProvisioningUserPassword,
            CertAcmeServer,
            CertAcmeAccountKeyPem,
            CertEmail,
            MailTransport,
            MailFromAddress,
            MailFromName,
            MailSmtpHost,
            MailSmtpPort,
            MailSmtpUsername,
            MailSmtpPassword,
            MailVoicemailCallbackToken,
            ParkingAudio,
            ParkingDtmfCode,
            ParkingEnabled,
            ParkingMusicClass,
            ParkingSlots,
            ParkingTimeout,
            SystemNtpServer,
            SystemHostname,
            SystemTimezone,
            WebRequestLog,
        };

        private static readonly HashSet<string> SecretKeys = new(StringComparer.Ordinal)
        {
            AmiSecret,
            ProvisioningPassword,
            ProvisioningAdminPassword,
            ProvisioningUserPassword,
            CertAcmeAccountKeyPem,
            MailSmtpPassword,
            MailVoicemailCallbackToken,
        };

        /// <summary>
        /// What reads each key, checked against the code that actually reads it rather than
        /// against the name it happens to start with (D103). Every key in
        /// <see cref="KnownKeys"/> is listed here, and a test says so.
        ///
        /// Asterisk: something in <c>ConfigApplier.Render</c> puts it in a conf file, or the
        /// applier itself needs it to do its job. Phones: only a phone's generated config, which
        /// is rendered per request by the provisioning controllers, so there is nothing on disk to
        /// apply. App: nothing but this web application ever reads it.
        /// </summary>
        private static readonly Dictionary<string, SettingScope> Scopes = new(StringComparer.Ordinal)
        {
            // Where the applier writes, and the directory the pjsip TLS certificate is named from.
            [AsteriskConfDirectory] = SettingScope.Asterisk,

            // Nothing generated reads this back — it is the directory the Logs page reads from,
            // not a directory Asterisk is told to use, so there is nothing an apply would write.
            [AsteriskLogDirectory] = SettingScope.App,

            // Host, port, username and secret are all in the generated manager.conf: the renderer
            // writes the port and the account, and refuses to write a file whose host is not the
            // loopback address AMI is bound to (D32).
            [AmiHost] = SettingScope.Asterisk,
            [AmiPort] = SettingScope.Asterisk,
            [AmiUsername] = SettingScope.Asterisk,
            [AmiSecret] = SettingScope.Asterisk,

            // The one AMI key no file carries: how long our own client waits for an answer.
            [AmiTimeoutSeconds] = SettingScope.App,

            // Every Sip key is read by AsteriskSettings.Transport, and so lands in pjsip.conf or
            // rtp.conf.
            [SipBindAddress] = SettingScope.Asterisk,
            [SipPort] = SettingScope.Asterisk,
            [SipTcpPort] = SettingScope.Asterisk,
            [SipTlsPort] = SettingScope.Asterisk,
            [SipStunServer] = SettingScope.Asterisk,
            [SipCodecs] = SettingScope.Asterisk,
            [SipLocalNets] = SettingScope.Asterisk,
            [SipExternalAddress] = SettingScope.Asterisk,
            [SipMaxContacts] = SettingScope.Asterisk,

            // The provisioning gate and the two device passwords are read by the Polycom and
            // Yealink controllers only; no conf file has ever carried them (D79, D84).
            [ProvisioningUsername] = SettingScope.Phones,
            [ProvisioningPassword] = SettingScope.Phones,
            [ProvisioningAdminPassword] = SettingScope.Phones,
            [ProvisioningUserPassword] = SettingScope.Phones,

            // Ordering details, read by the certificate service and nothing else. The certificate
            // it orders is a row in Certificates, and that repository raises the marker (D101), so
            // the file Asterisk reads is still applied like any other change.
            [CertAcmeServer] = SettingScope.App,
            [CertAcmeAccountKeyPem] = SettingScope.App,
            [CertEmail] = SettingScope.App,

            // Mail is sent by this application, over Graph or SMTP, not by Asterisk (D115).
            // Nothing in a generated conf file names any of these, so none of them is an apply.
            // Voicemail email uses the SMTP half of them too, but not through a conf file: the
            // generated voicemail.conf names a fixed mailcmd script, and the script's own copy of
            // the relay details is written the moment a setting is saved rather than at apply
            // time, so these stay App-scoped (D126).
            [MailTransport] = SettingScope.App,
            [MailFromAddress] = SettingScope.App,
            [MailFromName] = SettingScope.App,
            [MailSmtpHost] = SettingScope.App,
            [MailSmtpPort] = SettingScope.App,
            [MailSmtpUsername] = SettingScope.App,
            [MailSmtpPassword] = SettingScope.App,

            // The callback token is read by this application's own notify endpoint and written
            // into Config/mail.json for the script, the same way the relay details are (D129).
            // No generated conf file names it, so it is not an apply either.
            [MailVoicemailCallbackToken] = SettingScope.App,

            // Every Parking key lands in a generated conf file: the feature code in features.conf,
            // the slots and the timeout in res_parking.conf, the audio choice in res_parking.conf
            // (as parkedmusicclass, or its absence), and whether any of it exists at all in the
            // generated dialplan's slot routes (D119). All of it therefore needs an apply.
            [ParkingAudio] = SettingScope.Asterisk,
            [ParkingDtmfCode] = SettingScope.Asterisk,
            [ParkingEnabled] = SettingScope.Asterisk,
            [ParkingMusicClass] = SettingScope.Asterisk,
            [ParkingSlots] = SettingScope.Asterisk,
            [ParkingTimeout] = SettingScope.Asterisk,

            // Phones are told where to get the time from; nothing else reads it.
            [SystemNtpServer] = SettingScope.Phones,

            // Phones are told which host to reach the PBX on; nothing in a conf file reads it.
            [SystemHostname] = SettingScope.Phones,

            // Read by both: every GotoIfTime in the generated dialplan names the zone (D74), and a
            // Polycom phone is given its offset. Asterisk wins, because that half needs an apply.
            [SystemTimezone] = SettingScope.Asterisk,

            // Kestrel's own request log, which nothing outside this process reads or is told about
            // (D116). Not an apply — but not "takes effect straight away" either, which is why the
            // catalog's description says out loud that the service has to be restarted.
            [WebRequestLog] = SettingScope.App,
        };

        public static IReadOnlyCollection<string> All => KnownKeys;

        /// <summary>
        /// Every key with the scope it was classified with. The map itself rather than a question
        /// per key, so that "is there a key nobody classified?" is a question that can be asked —
        /// which is what stops <see cref="ScopeOf"/>'s fallback from quietly covering for a key
        /// somebody added and forgot (D103).
        /// </summary>
        public static IReadOnlyDictionary<string, SettingScope> AllScopes => Scopes;

        public static bool IsKnown(string key) => KnownKeys.Contains(key);

        /// <summary>Whether a value is a credential, so callers know not to log or display it.</summary>
        public static bool IsSecret(string key) => SecretKeys.Contains(key);

        /// <summary>
        /// What reads this key (D103). An unlisted key counts as <see cref="SettingScope.Asterisk"/>:
        /// a key added above and forgotten here then lights the apply button for nothing, which
        /// costs an apply that writes no files, where the other way round would leave Asterisk
        /// running config nobody was told had changed.
        /// </summary>
        public static SettingScope ScopeOf(string key) =>
            Scopes.TryGetValue(key, out var scope) ? scope : SettingScope.Asterisk;
    }
}
