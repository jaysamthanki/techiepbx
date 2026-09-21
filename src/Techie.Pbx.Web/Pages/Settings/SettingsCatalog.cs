using System.Globalization;
using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Diagnostics;
using Techie.Pbx.Core.Mail;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// Every key the settings page shows, with the words that explain it and the default it falls
    /// back to. The defaults are read from the objects that actually apply them — AmiSettings,
    /// PjsipTransport, AsteriskSettings — rather than typed out again here, so the page cannot
    /// claim a default the code does not use (D67).
    ///
    /// <see cref="SettingsKeys.All"/> is the list; a key added there with no entry here still
    /// appears on the page, with no description, which is the loud kind of gap.
    /// </summary>
    public static class SettingsCatalog
    {
        private static readonly AmiSettings AmiDefaults = new();
        private static readonly PjsipTransport TransportDefaults = new();

        /// <summary>
        /// On and off, with off first. The form's dropdown has no blank entry and treats the first
        /// choice as the default (D75), and parking is off until somebody turns it on — the other
        /// way round from <see cref="Toggles.All"/>, which is ordered for a setting that is on.
        /// </summary>
        private static readonly IReadOnlyList<string> ParkingToggles = new[] { Toggles.Off, Toggles.On };

        private static readonly Dictionary<string, SettingDescriptor> Descriptors = new[]
        {
            new SettingDescriptor
            {
                Default = AsteriskSettings.DefaultConfDirectory,
                Description = "Where the generated conf files are written. Only change it to test against a copy of Asterisk's config.",
                Key = SettingsKeys.AsteriskConfDirectory,
                Sample = "/etc/asterisk",
            },
            new SettingDescriptor
            {
                Default = LogTail.DefaultLogDirectory,
                Description = "Where Asterisk writes its logs. The Logs page reads messages.log and security.log from here; only change it to match a site that has moved them.",
                Key = SettingsKeys.AsteriskLogDirectory,
                Sample = "/var/log/asterisk",
            },
            new SettingDescriptor
            {
                Default = AmiDefaults.Host,
                Description = "Where Asterisk's manager interface is. Asterisk binds it to localhost, so this is only different if Asterisk is on another machine.",
                Key = SettingsKeys.AmiHost,
                Sample = "127.0.0.1",
            },
            new SettingDescriptor
            {
                Default = Number(AmiDefaults.Port),
                Description = "The manager interface port. Written into manager.conf, so both ends move together.",
                Key = SettingsKeys.AmiPort,
                Sample = "5038",
            },
            new SettingDescriptor
            {
                Description = "The account this app logs into AMI with. Written into manager.conf; without it, apply and live status cannot work.",
                Key = SettingsKeys.AmiUsername,
                Sample = "tnpbx",
            },
            new SettingDescriptor
            {
                Description = "The password for that AMI account. Stored here and written into manager.conf; the table shows dots, the edit form shows it.",
                Key = SettingsKeys.AmiSecret,
            },
            new SettingDescriptor
            {
                Default = Number(AmiDefaults.TimeoutSeconds),
                Description = "How long to wait for AMI to answer, in seconds. 0 waits forever.",
                Key = SettingsKeys.AmiTimeoutSeconds,
                Sample = "10",
            },
            new SettingDescriptor
            {
                Default = TransportDefaults.BindAddress,
                Description = "Which address Asterisk listens for SIP on. 0.0.0.0 is every interface.",
                Key = SettingsKeys.SipBindAddress,
                Sample = "0.0.0.0",
            },
            new SettingDescriptor
            {
                Default = Number(TransportDefaults.Port),
                Description = "The SIP port phones and trunks connect to.",
                Key = SettingsKeys.SipPort,
                Sample = "5060",
            },
            new SettingDescriptor
            {
                Description = "The TCP port for SIP. Blank means no TCP transport is generated at all; leave it blank unless a phone or provider needs TCP.",
                Key = SettingsKeys.SipTcpPort,
                Sample = "5060",
            },
            new SettingDescriptor
            {
                Description = "The TLS port for SIP. Stored only: no TLS transport is generated yet, because there is no certificate management, and a TLS transport without a certificate stops Asterisk loading the file (D71).",
                Key = SettingsKeys.SipTlsPort,
                Sample = "5061",
            },
            new SettingDescriptor
            {
                Description = "The STUN server media asks what its public address looks like. Blank means no STUN and no ICE, which is right for a server with a public address of its own.",
                Key = SettingsKeys.SipStunServer,
                Sample = "stun.l.google.com:19302",
            },
            new SettingDescriptor
            {
                Default = SipCodecs.Default,
                Description = $"The codecs extensions are offered, in preference order. Only the codecs this system loads: {string.Join(", ", SipCodecs.Allowed)}.",
                Key = SettingsKeys.SipCodecs,
                Sample = SipCodecs.Default,
            },
            new SettingDescriptor
            {
                Default = Number(TransportDefaults.MaxContacts),
                Description = "How many devices may register to one extension at once, for every extension — an office phone and a laptop softphone on the same number is 2. A call rings every registered device. 1 means a new registration replaces the old one.",
                Key = SettingsKeys.SipMaxContacts,
                Sample = "2",
            },
            new SettingDescriptor
            {
                Description = "The private networks this server is on, comma separated in CIDR form. Required when an external address is set, so Asterisk knows which calls are local.",
                Key = SettingsKeys.SipLocalNets,
                Sample = "10.8.20.0/24, 192.168.0.0/16",
            },
            new SettingDescriptor
            {
                Description = "The public IP address when this server is behind 1:1 NAT. Leave blank when it is not: setting it wrongly breaks audio.",
                Key = SettingsKeys.SipExternalAddress,
                Sample = "203.0.113.10",
            },
            new SettingDescriptor
            {
                Description = "The username a desk phone presents to fetch its configuration. It is the user half of the user:pass in the DHCP option 160 URL you give the phones — http://user:pass@this-server/polycom — so keep it to letters, digits, dots, dashes and underscores. Blank turns provisioning off: with no username and password stored, every provisioning request is refused.",
                Key = SettingsKeys.ProvisioningUsername,
                Sample = "phones",
            },
            new SettingDescriptor
            {
                Description = "The password half of that same DHCP option 160 URL. Stored here; the table shows dots, the edit form shows it. At least 8 characters, and only ones that need no escaping in a URL: letters, digits and . - _ ~",
                Key = SettingsKeys.ProvisioningPassword,
            },
            new SettingDescriptor
            {
                Description = "The password for the Polycom web UI's built-in \"Polycom\" (admin) account, written into every phone's config. This app also uses it to push a config reload or reboot to a phone over that same web UI when you save or ask for one. Stored here; the table shows dots, the edit form shows it.",
                Key = SettingsKeys.ProvisioningAdminPassword,
            },
            new SettingDescriptor
            {
                Description = "The password for the Polycom web UI's built-in \"User\" account, written into every phone's config. Stored here; the table shows dots, the edit form shows it.",
                Key = SettingsKeys.ProvisioningUserPassword,
            },
            new SettingDescriptor
            {
                Choices = AcmeServers.All,
                Default = AcmeServers.Default,
                Description = "Where certificates are ordered from. Leave it on production unless you are testing the ordering itself: staging issues certificates nothing trusts, which is what makes it safe to order from repeatedly.",
                Key = SettingsKeys.CertAcmeServer,
                Sample = AcmeServers.LetsEncrypt,
            },
            new SettingDescriptor
            {
                Description = "The ACME account key, generated the first time a certificate is ordered and reused for every renewal after it. There is normally no reason to touch this; the edit form shows it. Clearing it makes the next order register a new account.",
                Key = SettingsKeys.CertAcmeAccountKeyPem,
            },
            new SettingDescriptor
            {
                Description = "The contact address the Let's Encrypt account is registered with. Required before a certificate can be ordered: it is where expiry warnings go if renewal ever stops working.",
                Key = SettingsKeys.CertEmail,
                Sample = "admin@example.com",
            },
            new SettingDescriptor
            {
                Choices = MailTransports.All,
                Description = "How this system sends mail. Graph uses this app's own Entra registration to send as a mailbox in your tenant; SMTP submits to a relay. Reset it to default to leave the choice open, which means Graph where this server has an Entra client secret and no mail at all where it has not.",
                Key = SettingsKeys.MailTransport,
                Sample = MailTransports.Graph,
            },
            new SettingDescriptor
            {
                Description = "The address mail is sent from. On Graph this has to be a real mailbox in the tenant, because Graph sends as it — a shared mailbox such as pbx@example.com is the usual choice. On SMTP it is whatever address your relay lets this server send as.",
                Key = SettingsKeys.MailFromAddress,
                Sample = "pbx@example.com",
            },
            new SettingDescriptor
            {
                Description = "The display name beside the from address. Used on SMTP; on Graph the sending mailbox's own display name is what recipients see, so this has no effect there.",
                Key = SettingsKeys.MailFromName,
                Sample = "Techie PBX",
            },
            new SettingDescriptor
            {
                Description = "The relay this server submits mail to, when the transport is SMTP. A hostname or IP address on its own — no scheme and no port.",
                Key = SettingsKeys.MailSmtpHost,
                Sample = "smtp.sendgrid.net",
            },
            new SettingDescriptor
            {
                Default = Number(MailSettings.DefaultSmtpPort),
                Description = "The relay's submission port. 587 with STARTTLS is what every hosted relay uses; submission is always encrypted, so a relay that only offers plain text on 25 will not work.",
                Key = SettingsKeys.MailSmtpPort,
                Sample = "587",
            },
            new SettingDescriptor
            {
                Description = "The username this server authenticates to the relay with. On SendGrid it is literally the word apikey. Blank submits without authenticating, which only an internal relay that trusts this server by address will accept.",
                Key = SettingsKeys.MailSmtpUsername,
                Sample = "apikey",
            },
            new SettingDescriptor
            {
                Description = "The password for that relay account — a SendGrid API key, a Google app password, or whatever your own relay issued. Stored here; the table shows dots, the edit form shows it.",
                Key = SettingsKeys.MailSmtpPassword,
            },
            new SettingDescriptor
            {
                Choices = ParkingAudio.All,
                Default = ParkingAudio.Silence,
                Description = "What a parked caller hears while they wait: nothing, or the music on hold class named below. With no tracks in that class it is silence either way.",
                Key = SettingsKeys.ParkingAudio,
                Sample = ParkingAudio.MusicOnHold,
            },
            new SettingDescriptor
            {
                Default = ParkingSettings.DefaultMusicClass,
                Description = "Which music on hold class a parked caller hears, by name, when the setting above says moh. The classes are on the Music on hold page; a name that matches none of them is silence, because Asterisk finds nothing to play.",
                Key = SettingsKeys.ParkingMusicClass,
                Sample = ParkingSettings.DefaultMusicClass,
            },
            new SettingDescriptor
            {
                Default = ParkingSettings.DefaultDtmfCode,
                Description = "The keys a user presses during a call to park it: a star and one or two digits. It is matched inside a live call rather than dialled, so it cannot collide with an extension or a feature code.",
                Key = SettingsKeys.ParkingDtmfCode,
                Sample = ParkingSettings.DefaultDtmfCode,
            },
            new SettingDescriptor
            {
                Choices = ParkingToggles,
                Default = Toggles.Off,
                Description = "Whether call parking exists at all. Off means no park feature code, no parking lot and no slot numbers in the dialplan — nothing to dial by accident.",
                Key = SettingsKeys.ParkingEnabled,
                Sample = Toggles.On,
            },
            new SettingDescriptor
            {
                Default = Number(ParkingSettings.DefaultSlots),
                Description = $"How many parking slots there are, {SettingsValidation.MinParkingSlots} to {SettingsValidation.MaxParkingSlots}. A slot is picked up by dialling its number, so each one is a single digit — which is what keeps them clear of extensions, which are three digits or more.",
                Key = SettingsKeys.ParkingSlots,
                Sample = Number(ParkingSettings.DefaultSlots),
            },
            new SettingDescriptor
            {
                Default = Number(ParkingSettings.DefaultTimeoutSeconds),
                Description = $"How long a call stays parked before it rings back the phone that parked it, in seconds ({SettingsValidation.MinParkingTimeoutSeconds} to {SettingsValidation.MaxParkingTimeoutSeconds}).",
                Key = SettingsKeys.ParkingTimeout,
                Sample = Number(ParkingSettings.DefaultTimeoutSeconds),
            },
            new SettingDescriptor
            {
                Default = AsteriskSettings.DefaultNtpServer,
                Description = "The NTP server phones set their clock from, written into every phone's config. Point this at an internal time source if this site has one; otherwise leave it at the public default.",
                Key = SettingsKeys.SystemNtpServer,
                Sample = AsteriskSettings.DefaultNtpServer,
            },
            new SettingDescriptor
            {
                Description = "The hostname phones are told to reach the PBX on — the SIP server address and the provisioning URL in their configs. Leave it empty to keep using whatever host the phone asked on; set it when the site has a real name, so a phone that first contacts this box by IP still ends up on the name.",
                Key = SettingsKeys.SystemHostname,
                Sample = "pbx.example.com",
            },
            new SettingDescriptor
            {
                Choices = SystemTimezones.All,
                Default = AsteriskSettings.DefaultTimezone,
                Description = "The zone a time condition's open hours and holidays are written in. Every check in the generated dialplan names it, so Asterisk evaluates the hours in that zone with daylight saving included, whatever the server's clock says — the clock itself is meant to be UTC (D74).",
                Key = SettingsKeys.SystemTimezone,
                Sample = "Europe/London",
            },
            new SettingDescriptor
            {
                Choices = Toggles.All,
                Default = Toggles.On,
                Description = "Whether every web request is written to the request log: one line each with the time, " +
                    "the caller's address, the port, the path, the status and how long it took. It is how you tell " +
                    "whether a desk phone reached this server at all, and what it was answered with. Read it on the " +
                    "Logs page. This one is read when the service starts, so restart tnpbx-web after changing it.",
                Key = SettingsKeys.WebRequestLog,
                Sample = Toggles.On,
            },
        }.ToDictionary(descriptor => descriptor.Key, StringComparer.Ordinal);

        /// <summary>Every known key, in the order the page lists them.</summary>
        public static IReadOnlyList<SettingDescriptor> All { get; } = SettingsKeys.All
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(For)
            .ToList();

        /// <summary>
        /// The entry for one key. A key with no entry gets a bare one rather than null, so a key
        /// added to <see cref="SettingsKeys"/> and forgotten here is still editable.
        /// </summary>
        public static SettingDescriptor For(string key) =>
            Descriptors.TryGetValue(key, out var descriptor) ? descriptor : new SettingDescriptor { Key = key };

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
