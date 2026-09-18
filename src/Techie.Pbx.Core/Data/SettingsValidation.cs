using System.Net;
using System.Text.RegularExpressions;
using Techie.Pbx.Core.Models;

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

        /// <summary>A zone name that is certain to be in any real tzdata, used to tell a machine
        /// with no zone database apart from an admin's typo.</summary>
        private const string ProbeZone = "Europe/London";

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
        /// The shape of an IANA zone name: letters, digits and the few punctuation marks one is
        /// made of. This is the check that keeps a stray character out of a conf file (D65); that
        /// the zone actually exists is a separate, stricter question asked when it is written.
        /// </summary>
        public static bool IsZoneName(string value) =>
            value.Length <= 64 &&
            value.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '+');

        /// <summary>
        /// Whether this machine's zone database can be read at all. A machine without tzdata would
        /// otherwise reject every zone an admin typed, including the right one, so on such a
        /// machine the name is accepted on its shape alone.
        /// </summary>
        public static bool ZoneDatabaseIsReadable() => TimeZoneInfo.TryFindSystemTimeZoneById(ProbeZone, out _);

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

        private static void Timezone(List<string> errors, string text)
        {
            if (!IsZoneName(text))
            {
                errors.Add($"'{text}' is not an IANA zone name. Write it as Region/City, e.g. Europe/London.");
                return;
            }

            // The system's own zone database is the list, rather than one of ours to keep current.
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(text, out _) && ZoneDatabaseIsReadable())
                errors.Add($"This server has no timezone called '{text}'. Write it as Region/City, e.g. Europe/London.");
        }

        /// <summary>A hostname or IP address, the same shape a trunk's server host has to be.</summary>
        [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9.\-]{0,253}[A-Za-z0-9])?$")]
        private static partial Regex HostPattern();
    }
}
