using System.Globalization;
using System.Text.RegularExpressions;

// The property CertificatePem would otherwise hide the class of the same name inside this type.
using Pem = Techie.Pbx.Core.Security.CertificatePem;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// One TLS certificate, ordered from Let's Encrypt over ACME and stored here in full: the leaf,
    /// its issuers and its private key (D97). Two things read it — Kestrel, for the admin UI on
    /// 443 (D99), and the generated pjsip TLS transport, for SIP over TLS (D101) — and both take
    /// it from this row rather than from a file somebody has to keep in step.
    ///
    /// A row exists before it has a certificate in it: ordering is a network conversation that can
    /// fail, so the name and hostnames are stored first and <see cref="LastError"/> says what
    /// happened if the order did not finish.
    /// </summary>
    public partial class Certificate
    {
        /// <summary>
        /// The most hostnames one certificate may cover. Let's Encrypt allows a hundred; this is a
        /// PBX with a web UI and a SIP port, so a much smaller number is a typo catcher rather than
        /// a limitation.
        /// </summary>
        public const int MaxHostnames = 10;

        /// <summary>
        /// How close to expiry a certificate is renewed, in days. Let's Encrypt issues for 90 days
        /// and recommends renewing at 30, which leaves a month of failed attempts before anything
        /// actually stops working (D100).
        /// </summary>
        public const int RenewalThresholdDays = 30;

        public long CertificateID { get; set; }

        /// <summary>The leaf certificate, PEM encoded, or empty when nothing has been issued yet.</summary>
        public string CertificatePem { get; set; } = "";

        /// <summary>The issuers above the leaf, PEM encoded, in order. Empty is possible.</summary>
        public string ChainPem { get; set; } = "";

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// When the leaf stops being valid, as UTC text ("u" format), or empty when there is no
        /// certificate yet. Stored rather than derived so a list can be sorted without parsing
        /// every PEM.
        /// </summary>
        public string ExpiresUtc { get; set; } = "";

        /// <summary>The names this certificate is for, comma separated. The first is the subject.</summary>
        public string Hostnames { get; set; } = "";

        /// <summary>
        /// The private key, PEM encoded. As sensitive as a SIP secret: never logged, never rendered
        /// into a page, and only ever written to disk inside the combined PEM Asterisk reads.
        /// </summary>
        public string KeyPem { get; set; } = "";

        /// <summary>Why the last order or renewal failed, or null if the last one worked.</summary>
        public string? LastError { get; set; }

        /// <summary>What an admin calls this certificate. Unique, and only a label.</summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Everything Asterisk needs in one file: certificate, issuers and key (D101). Empty when
        /// there is nothing to write yet.
        /// </summary>
        public string CombinedPem =>
            this.HasKeyPair ? Pem.Combine(this.CertificatePem, this.ChainPem, this.KeyPem) : "";

        /// <summary>When the leaf stops being valid, or null when there is no certificate yet.</summary>
        public DateTimeOffset? Expires =>
            DateTimeOffset.TryParse(
                this.ExpiresUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value)
                ? value
                : null;

        /// <summary>Whether an order has actually produced something: a certificate and its key.</summary>
        public bool HasKeyPair => this.CertificatePem.Length > 0 && this.KeyPem.Length > 0;

        /// <summary>
        /// How many days are left, rounded down, or null when there is no certificate. Negative
        /// once it has expired, because "-3 days" is more use to an admin than "expired".
        /// </summary>
        public int? DaysUntilExpiry(DateTimeOffset now) =>
            this.Expires is { } expires ? (int)Math.Floor((expires - now).TotalDays) : null;

        /// <summary>The hostnames as a list, trimmed and lower-cased, in the order they are stored.</summary>
        public List<string> HostnameList() => ParseHostnames(this.Hostnames);

        /// <summary>
        /// Whether this certificate can be served right now: switched on, actually issued, and not
        /// expired. What decides whether Kestrel binds 443 (D99) and whether pjsip gets a TLS
        /// transport (D101) — an expired certificate is worse than none, because Asterisk would
        /// load a transport nothing can complete a handshake with.
        /// </summary>
        public bool IsUsable(DateTimeOffset now) =>
            this.Enabled && this.HasKeyPair && this.Expires is { } expires && expires > now;

        /// <summary>
        /// Whether the daily renewal service should order this one again (D100): a row that is
        /// switched on and either has never been issued — a first order that failed, retried — or
        /// is inside <see cref="RenewalThresholdDays"/> of expiry.
        /// </summary>
        public bool NeedsRenewal(DateTimeOffset now)
        {
            if (!this.Enabled)
                return false;

            if (!this.HasKeyPair)
                return true;

            return this.DaysUntilExpiry(now) <= RenewalThresholdDays;
        }

        /// <summary>
        /// A hostname ACME can answer an HTTP-01 challenge for: an ordinary DNS name. Wildcards are
        /// refused because HTTP-01 cannot prove one — that needs DNS-01, which is not built (D97).
        /// </summary>
        public static bool IsValidHostname(string hostname) =>
            hostname.Length <= 253 && HostnamePattern().IsMatch(hostname);

        /// <summary>The stored form of a hostname list: lower-case, trimmed, comma separated.</summary>
        public static string NormalizeHostnames(string hostnames) => string.Join(",", ParseHostnames(hostnames));

        /// <summary>
        /// Splits a hostname list as a person may have typed it — commas, spaces or one per line —
        /// into names. Lower-cased, because a certificate's names are.
        /// </summary>
        public static List<string> ParseHostnames(string hostnames) =>
            (hostnames ?? "")
                .Split(new[] { ',', ' ', '\t', '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(h => h.ToLowerInvariant())
                .ToList();

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(this.Name))
                errors.Add("Name is required.");
            else if (this.Name.Length > 64)
                errors.Add("Name must be 64 characters or fewer.");
            else if (!NamePattern().IsMatch(this.Name))
                errors.Add("Name may only contain letters, digits, spaces and . - _");

            var hostnames = this.HostnameList();

            if (hostnames.Count == 0)
                errors.Add("At least one hostname is required.");
            else if (hostnames.Count > MaxHostnames)
                errors.Add($"A certificate may cover at most {MaxHostnames} hostnames.");

            foreach (var hostname in hostnames.Where(h => !IsValidHostname(h)))
            {
                errors.Add(hostname.StartsWith('*')
                    ? $"'{hostname}' is a wildcard. Wildcards need a DNS challenge, which this system does not do — name each host instead."
                    : $"'{hostname}' is not a hostname, e.g. pbx.example.com.");
            }

            if (hostnames.Count != hostnames.Distinct(StringComparer.Ordinal).Count())
                errors.Add("Each hostname may only be named once.");

            return errors;
        }

        /// <summary>
        /// A DNS name with at least one dot in it: Let's Encrypt will not issue for a bare label,
        /// so accepting one here would only produce a failed order.
        /// </summary>
        [GeneratedRegex(@"^[a-z0-9]([a-z0-9\-]{0,62}[a-z0-9])?(\.[a-z0-9]([a-z0-9\-]{0,62}[a-z0-9])?)+\z")]
        private static partial Regex HostnamePattern();

        [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} .\-_]{0,63}\z")]
        private static partial Regex NamePattern();
    }
}
