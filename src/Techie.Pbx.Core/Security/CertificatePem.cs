using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Techie.Pbx.Core.Security
{
    /// <summary>
    /// The PEM handling every certificate consumer shares: splitting what an ACME order returns
    /// into the leaf and its issuers, reading the expiry off a leaf, and building the one combined
    /// file Asterisk reads (D101).
    ///
    /// Pure text and crypto, no I/O and no storage, so the web app, the renderer and the tests all
    /// ask the same question the same way. Nothing here logs: every value it touches is either a
    /// certificate or a private key.
    /// </summary>
    public static class CertificatePem
    {
        /// <summary>How an expiry is stored in the database and compared as text.</summary>
        public const string TimestampFormat = "u";

        private const string BeginCertificate = "-----BEGIN CERTIFICATE-----";
        private const string EndCertificate = "-----END CERTIFICATE-----";

        /// <summary>
        /// Certificate, then any issuers, then the private key, in one file. Asterisk 22's pjsip
        /// TLS transport is happy to take the lot from one path, which means one file to write, one
        /// mode to get right and no way for a cert and its key to be applied separately (D101).
        /// </summary>
        public static string Combine(string certificatePem, string chainPem, string keyPem)
        {
            var sb = new StringBuilder();

            foreach (var part in new[] { certificatePem, chainPem, keyPem })
            {
                var text = (part ?? "").Trim();
                if (text.Length == 0)
                    continue;

                sb.Append(text.ReplaceLineEndings("\n")).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// When the first certificate in this PEM stops being valid, or null when there is no
        /// certificate in it to ask. Never throws: a row whose PEM is unreadable is a row the UI
        /// reports, not an exception in the middle of an apply.
        /// </summary>
        public static DateTimeOffset? Expiry(string certificatePem)
        {
            try
            {
                using var certificate = X509Certificate2.CreateFromPem(certificatePem);
                return new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            }
            catch (Exception ex) when (ex is ArgumentException or CryptographicException)
            {
                return null;
            }
        }

        /// <summary>
        /// The certificate blocks in a PEM, in the order they appear. Used to split what an ACME
        /// order hands back (leaf first, then issuers) without depending on the ACME library's own
        /// object model.
        /// </summary>
        public static List<string> Certificates(string pem)
        {
            var blocks = new List<string>();
            var text = (pem ?? "").ReplaceLineEndings("\n");
            var at = 0;

            while (true)
            {
                var start = text.IndexOf(BeginCertificate, at, StringComparison.Ordinal);
                if (start < 0)
                    break;

                var end = text.IndexOf(EndCertificate, start, StringComparison.Ordinal);
                if (end < 0)
                    break;

                end += EndCertificate.Length;
                blocks.Add(text[start..end].Trim() + "\n");
                at = end;
            }

            return blocks;
        }

        /// <summary>
        /// A certificate and its private key as one object, for Kestrel. Returns null rather than
        /// throwing when the pair cannot be loaded, because the answer to "this stored certificate
        /// is unusable" is to start on the bootstrap port and say so (D99), not to refuse to start.
        /// </summary>
        public static X509Certificate2? Load(string certificatePem, string keyPem)
        {
            try
            {
                return X509Certificate2.CreateFromPem(certificatePem, keyPem);
            }
            catch (Exception ex) when (ex is ArgumentException or CryptographicException)
            {
                return null;
            }
        }

        /// <summary>
        /// The issuers of a PEM chain as certificates Kestrel can send alongside the leaf. Empty
        /// when there are none, which is what a self-signed test certificate has.
        /// </summary>
        public static X509Certificate2Collection LoadChain(string chainPem)
        {
            var chain = new X509Certificate2Collection();

            foreach (var block in Certificates(chainPem))
            {
                try
                {
                    chain.Add(X509Certificate2.CreateFromPem(block));
                }
                catch (Exception ex) when (ex is ArgumentException or CryptographicException)
                {
                    // An unreadable issuer is not worth failing a start over: the leaf still
                    // proves the hostname, the client may already have the issuer.
                }
            }

            return chain;
        }

        /// <summary>
        /// Splits what an ACME order returns into the leaf certificate and everything above it.
        /// The order is the one every ACME server uses — leaf first — and both halves are stored
        /// separately so the row can say what it is a certificate <em>for</em> without parsing a
        /// chain every time.
        /// </summary>
        public static (string Certificate, string Chain) SplitChain(string pem)
        {
            var blocks = Certificates(pem);

            if (blocks.Count == 0)
                return ("", "");

            return (blocks[0], string.Concat(blocks.Skip(1)));
        }

        /// <summary>An expiry as the database stores it: UTC, sortable as text.</summary>
        public static string Timestamp(DateTimeOffset value) =>
            value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
    }
}
