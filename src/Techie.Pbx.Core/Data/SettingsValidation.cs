using System.Net;
using System.Text.RegularExpressions;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// What a setting's value is allowed to be, per key. Pure functions over strings, so the
    /// settings page and <see cref="SettingsRepository"/> ask the same question the same way and
    /// nothing can be written that the loader would then quietly ignore.
    ///
    /// A blank value means "not set" everywhere in this system (AsteriskSettings reads it that
    /// way), so the per-key rules only apply to a value that has something in it: clearing a box
    /// is how an admin puts a setting back to its default, never a validation error.
    /// </summary>
    public static partial class SettingsValidation
    {
        /// <summary>Longer than any of these values has any business being.</summary>
        public const int MaxValueLength = 1024;

        /// <summary>
        /// The shortest provisioning password. It guards an endpoint a phone on any network can
        /// reach and it is typed once, into a DHCP option, so there is no reason for a short one
        /// (D77).
        /// </summary>
        public const int MinProvisioningPasswordLength = 8;

        /// <summary>
        /// Every problem with this key and value; empty means it can be stored. Unknown keys are
        /// rejected here rather than only at the database, so the UI can say so too.
        /// </summary>
        public static List<string> Errors(string key, string? value)
        {
            var errors = new List<string>();

            if (!SettingsKeys.IsKnown(key))
                errors.Add($"'{key}' is not a known setting.");

            if (value == null)
            {
                errors.Add("Setting value is required.");
                return errors;
            }

            if (value.Length > MaxValueLength)
                errors.Add($"Setting value must be {MaxValueLength} characters or fewer.");

            var text = value.Trim();

            // Blank is how a setting goes back to its default, so there is nothing left to check.
            if (text.Length == 0)
                return errors;

            switch (key)
            {
                case SettingsKeys.AmiPort:
                    Port(errors, "AMI port", text);
                    break;

                case SettingsKeys.SipPort:
                    Port(errors, "SIP port", text);
                    break;

                case SettingsKeys.SipTcpPort:
                    Port(errors, "SIP TCP port", text);
                    break;

                case SettingsKeys.SipTlsPort:
                    Port(errors, "SIP TLS port", text);
                    break;

                case SettingsKeys.SipStunServer:
                    if (!IsStunServer(text))
                        errors.Add("STUN server must be a hostname or IP address, optionally with :port, e.g. stun.l.google.com:19302.");
                    break;

                case SettingsKeys.SipCodecs:
                    Codecs(errors, text);
                    break;

                case SettingsKeys.AmiTimeoutSeconds:
                    if (!int.TryParse(text, out var timeout) || timeout is < 0 or > 600)
                        errors.Add("AMI timeout must be a whole number of seconds between 0 and 600 (0 waits forever).");
                    break;

                case SettingsKeys.AmiHost:
                    if (text.Any(char.IsWhiteSpace))
                        errors.Add("AMI host must be an IP address or a hostname, with no spaces in it.");
                    break;

                case SettingsKeys.SipBindAddress:
                    if (!IPAddress.TryParse(text, out _))
                        errors.Add("SIP bind address must be an IP address, e.g. 0.0.0.0 for every interface.");
                    break;

                case SettingsKeys.SipExternalAddress:
                    if (!IPAddress.TryParse(text, out _))
                        errors.Add("External address must be an IP address, e.g. 203.0.113.10.");
                    break;

                case SettingsKeys.SipLocalNets:
                    LocalNets(errors, text);
                    break;

                case SettingsKeys.AsteriskConfDirectory:
                    if (!text.StartsWith('/'))
                        errors.Add("The config directory must be an absolute path, e.g. /etc/asterisk.");
                    break;

                case SettingsKeys.ProvisioningUsername:
                    if (!ProvisioningUsernamePattern().IsMatch(text))
                        errors.Add("Provisioning username must be 1 to 64 letters, digits, dots, dashes or underscores — it goes into the DHCP option 160 URL, where anything else would have to be escaped.");
                    break;

                case SettingsKeys.ProvisioningPassword:
                    if (!ProvisioningPasswordPattern().IsMatch(text))
                        errors.Add($"Provisioning password must be {MinProvisioningPasswordLength} to 64 letters, digits, dots, dashes, underscores or tildes — it goes into the DHCP option 160 URL, where a colon or an @ would split it.");
                    break;

                case SettingsKeys.ProvisioningAdminPassword:
                    if (!ProvisioningPasswordPattern().IsMatch(text))
                        errors.Add($"The Polycom web admin password must be {MinProvisioningPasswordLength} to 64 letters, digits, dots, dashes, underscores or tildes.");
                    break;

                case SettingsKeys.ProvisioningUserPassword:
                    if (!ProvisioningPasswordPattern().IsMatch(text))
                        errors.Add($"The Polycom web user password must be {MinProvisioningPasswordLength} to 64 letters, digits, dots, dashes, underscores or tildes.");
                    break;

                case SettingsKeys.CertAcmeServer:
                    if (!AcmeServers.IsKnown(text))
                        errors.Add($"The ACME server must be one of: {string.Join(", ", AcmeServers.All)}");
                    break;

                case SettingsKeys.CertAcmeAccountKeyPem:
                    if (!IsPrivateKeyPem(text))
                        errors.Add("The ACME account key must be a PEM private key. It is generated for you on the first order — there is normally no reason to type one in.");
                    break;

                case SettingsKeys.CertEmail:
                    if (!EmailPattern().IsMatch(text))
                        errors.Add("The certificate contact must be an email address, e.g. admin@example.com.");
                    break;

                case SettingsKeys.SystemNtpServer:
                    if (!HostPattern().IsMatch(text))
                        errors.Add("NTP server must be a hostname or IP address, e.g. pool.ntp.org.");
                    break;

                case SettingsKeys.SystemHostname:
                    if (text.Length != 0 && !HostPattern().IsMatch(text))
                        errors.Add("The hostname must be a bare hostname or IP address, e.g. pbx.example.com — no http:// and no path. Leave it empty to keep using whatever host the phone asked on.");
                    break;

                case SettingsKeys.SystemTimezone:
                    Timezone(errors, text);
                    break;
            }

            return errors;
        }

        /// <summary>
        /// A STUN server as rtp.conf takes it: a hostname or IP address, optionally with a port
        /// (D72). Public because the renderer's own settings object checks it the same way, so the
        /// value the page accepts and the value the file can carry are the same value.
        /// </summary>
        public static bool IsStunServer(string value)
        {
            var text = value.Trim();
            if (text.Length == 0 || text.Length > 255)
                return false;

            var separator = text.LastIndexOf(':');
            if (separator < 0)
                return HostPattern().IsMatch(text);

            var port = text[(separator + 1)..];

            return HostPattern().IsMatch(text[..separator]) &&
                int.TryParse(port, out var number) &&
                number is >= 1 and <= 65535;
        }

        /// <summary>
        /// Whether a value looks like a PEM private key, which is all that can honestly be checked
        /// without trying to use it: the ACME library is what decides whether the key is good, and
        /// it says so on the first order rather than here.
        /// </summary>
        public static bool IsPrivateKeyPem(string value)
        {
            var text = value.Trim();

            return text.StartsWith("-----BEGIN", StringComparison.Ordinal) &&
                text.Contains("PRIVATE KEY-----", StringComparison.Ordinal) &&
                text.EndsWith("-----", StringComparison.Ordinal);
        }

        /// <summary>
        /// The shape of an IANA zone name, which is <see cref="SystemTimezones.IsZoneName"/>: the
        /// check that keeps a stray character out of a conf file. Kept here because the renderer's
        /// settings object asks this question through this class, and one rule is what stops the
        /// two from drifting.
        /// </summary>
        public static bool IsZoneName(string value) => SystemTimezones.IsZoneName(value);

        /// <summary>
        /// Whether this machine's zone database can be read at all. A machine without tzdata would
        /// otherwise reject every zone an admin chose, including the right one, so on such a
        /// machine the name is accepted on its shape alone.
        /// </summary>
        public static bool ZoneDatabaseIsReadable() => SystemTimezones.IsListed;

        /// <summary>
        /// Only codecs whose modules are on the modules.conf allowlist, because a codec Asterisk
        /// has no module for is a call that fails at answer time rather than a setting that is
        /// obviously wrong (D73).
        /// </summary>
        private static void Codecs(List<string> errors, string text)
        {
            var codecs = SipCodecs.Parse(text);

            if (codecs.Count == 0)
            {
                errors.Add($"Codecs must name at least one of: {string.Join(", ", SipCodecs.Allowed)}.");
                return;
            }

            foreach (var codec in codecs.Where(c => !SipCodecs.IsAllowed(c)))
                errors.Add($"'{codec}' is not a codec this system loads. Choose from: {string.Join(", ", SipCodecs.Allowed)}.");

            if (codecs.Count != codecs.Distinct(StringComparer.Ordinal).Count())
                errors.Add("Each codec may only be named once.");
        }

        /// <summary>
        /// The same rule PjsipTransport applies when it renders pjsip.conf: full CIDR form,
        /// network address and all. Checked here so a value that would fail the next apply
        /// cannot be stored in the first place.
        /// </summary>
        private static void LocalNets(List<string> errors, string text)
        {
            var entries = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (entries.Length == 0)
            {
                errors.Add("Local networks must be one or more comma separated CIDR ranges, e.g. 10.8.20.0/24.");
                return;
            }

            foreach (var entry in entries)
            {
                if (!IPNetwork.TryParse(entry, out _))
                    errors.Add($"Local network '{entry}' must be in CIDR form, e.g. 10.8.20.0/24.");
            }
        }

        private static void Port(List<string> errors, string what, string text)
        {
            if (!int.TryParse(text, out var port) || port is < 1 or > 65535)
                errors.Add($"{what} must be a whole number between 1 and 65535.");
        }

        /// <summary>
        /// The zone is chosen from a list now rather than typed (D75), so the rule is membership of
        /// that list: it is the zone name that goes into every GotoIfTime a time condition writes,
        /// and a name Asterisk cannot resolve would make the rule never match (D74). The shape check
        /// comes first so that a value which arrived some other way is reported as the wrong kind of
        /// thing rather than as a zone this server happens not to have.
        /// </summary>
        private static void Timezone(List<string> errors, string text)
        {
            if (!IsZoneName(text))
            {
                errors.Add($"'{text}' is not an IANA zone name. Choose one from the list, e.g. Europe/London.");
                return;
            }

            // The system's own zone database is the list, rather than one of ours to keep current.
            if (!SystemTimezones.IsKnown(text) && SystemTimezones.IsListed)
                errors.Add($"This server has no timezone called '{text}'. Choose one from the list, e.g. Europe/London.");
        }

        /// <summary>
        /// An email address, checked loosely on purpose: the only thing this address is used for is
        /// the ACME account's expiry warnings, and a rule strict enough to be interesting would
        /// reject somebody's real address.
        /// </summary>
        [GeneratedRegex(@"^[^@\s]+@[A-Za-z0-9]([A-Za-z0-9.\-]{0,253}[A-Za-z0-9])?$")]
        private static partial Regex EmailPattern();

        /// <summary>A hostname or IP address, the same shape a trunk's server host has to be.</summary>
        [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9.\-]{0,253}[A-Za-z0-9])?$")]
        private static partial Regex HostPattern();

        /// <summary>
        /// URL-safe characters only, because <see cref="SettingsKeys.ProvisioningPassword"/> ends
        /// up inside a URL (D77). Reused for the two Polycom device account passwords too: they
        /// are written into an XML attribute rather than a URL, but the same narrow charset also
        /// keeps them clear of every character the conf renderers' own safety check refuses (D84).
        /// The lower bound is <see cref="MinProvisioningPasswordLength"/>, written out here because
        /// an attribute needs a literal.
        /// </summary>
        [GeneratedRegex(@"^[A-Za-z0-9._~\-]{8,64}$")]
        private static partial Regex ProvisioningPasswordPattern();

        [GeneratedRegex(@"^[A-Za-z0-9._\-]{1,64}$")]
        private static partial Regex ProvisioningUsernamePattern();
    }
}
