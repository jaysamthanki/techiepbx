using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using log4net;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Web.Certificates
{
    /// <summary>
    /// Orders and renews certificates from Let's Encrypt over ACME, in this process, with Certes
    /// (D97). No certbot, no second runtime, no shell: the whole exchange is a few HTTPS calls and
    /// one HTTP path this app answers itself (D98).
    ///
    /// Every order is the same conversation: register (or re-use) the account key, ask for the
    /// hostnames, publish the HTTP-01 answers into <see cref="AcmeChallengeStore"/>, tell the
    /// server to check them, wait, and then generate the certificate. What comes back is split
    /// into leaf, issuers and key and written to the row it was ordered for.
    ///
    /// Failure is a stored message rather than an exception: an order can fail because DNS is
    /// wrong, because port 80 is unreachable, or because a rate limit was hit, and all three are
    /// things an admin has to read. <see cref="Certificate.LastError"/> is where they read it.
    /// </summary>
    public class AcmeCertificateService
    {
        /// <summary>The key algorithm certificates are issued for. RSA, because a desk phone's TLS
        /// stack is the oldest thing that will ever talk to it, and 2048-bit RSA is what every one
        /// of them understands.</summary>
        public const KeyAlgorithm CertificateAlgorithm = KeyAlgorithm.RS256;

        /// <summary>
        /// The account key algorithm. Elliptic curve, because the PEM has to fit in a settings
        /// value, and because the account key only ever signs ACME requests.
        /// </summary>
        public const KeyAlgorithm AccountAlgorithm = KeyAlgorithm.ES256;

        /// <summary>How long to wait for the ACME server to check the challenges before giving up.</summary>
        public static readonly TimeSpan ValidationTimeout = TimeSpan.FromMinutes(2);

        /// <summary>How often the order is asked whether validation has finished.</summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

        private static readonly ILog Log = LogManager.GetLogger(typeof(AcmeCertificateService));

        private readonly CertificateRepository certificates;
        private readonly SettingsRepository settings;

        public AcmeCertificateService(Database database)
        {
            this.certificates = new CertificateRepository(database);
            this.settings = new SettingsRepository(database);
        }

        /// <summary>
        /// Orders (or renews, which is the same thing) the certificate for one row and stores the
        /// result on it. Always saves: on success the PEMs, the expiry and a cleared error; on
        /// failure the message and nothing else, so the certificate that is already there keeps
        /// working while somebody fixes whatever went wrong.
        /// </summary>
        public async Task<Certificate> Order(Certificate certificate)
        {
            var hostnames = certificate.HostnameList();
            var stored = this.settings.GetAll();
            var email = Text(stored, SettingsKeys.CertEmail);

            if (email.Length == 0)
                return this.Failed(certificate, $"Set {SettingsKeys.CertEmail} on the Settings page before ordering: Let's Encrypt requires a contact address.");

            if (hostnames.Count == 0)
                return this.Failed(certificate, "A certificate needs at least one hostname.");

            var server = Text(stored, SettingsKeys.CertAcmeServer);
            var directory = new Uri(AcmeServers.IsKnown(server) ? server : AcmeServers.Default);

            try
            {
                Log.Info($"Certificate '{certificate.Name}': ordering {string.Join(", ", hostnames)} from {directory}");

                var context = await this.Account(directory, email);
                var order = await context.NewOrder(hostnames);

                await Validate(order);

                var key = KeyFactory.NewKey(CertificateAlgorithm);
                var chain = await order.Generate(new CsrInfo { CommonName = hostnames[0] }, key);
                var (leaf, issuers) = CertificatePem.SplitChain(chain.ToPem());

                if (leaf.Length == 0)
                    return this.Failed(certificate, "The ACME server returned no certificate.");

                var expires = CertificatePem.Expiry(leaf);
                if (expires == null)
                    return this.Failed(certificate, "The certificate the ACME server returned could not be read.");

                certificate.CertificatePem = leaf;
                certificate.ChainPem = issuers;
                certificate.KeyPem = key.ToPem();
                certificate.ExpiresUtc = CertificatePem.Timestamp(expires.Value);
                certificate.LastError = null;

                this.certificates.Update(certificate);

                Log.Info($"Certificate '{certificate.Name}' issued, expires {certificate.ExpiresUtc}");
                return certificate;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return this.Failed(certificate, Describe(ex));
            }
            finally
            {
                // The tokens are only meaningful while the server is checking them.
                AcmeChallengeStore.Clear();
            }
        }

        /// <summary>
        /// What a failed order left behind, in words an admin can act on. Certes wraps the ACME
        /// server's own explanation, which is the useful half — "DNS problem: NXDOMAIN looking up A
        /// for ..." is the answer, "one or more domains had a problem" is not.
        /// </summary>
        private static string Describe(Exception ex)
        {
            if (ex is AcmeRequestException { Error: { } error } && !string.IsNullOrWhiteSpace(error.Detail))
                return error.Detail;

            return ex.Message;
        }

        /// <summary>A setting's value, trimmed, or empty when it is not set.</summary>
        private static string Text(IReadOnlyDictionary<string, string> settings, string key) =>
            settings.TryGetValue(key, out var value) ? value.Trim() : "";

        /// <summary>
        /// Answers every HTTP-01 challenge in the order and waits for the ACME server to agree.
        /// The answers go into the in-memory store the challenge endpoint reads (D98) — nothing is
        /// written to a web root, because there is no web root to write to.
        /// </summary>
        private static async Task Validate(IOrderContext order)
        {
            var authorizations = (await order.Authorizations()).ToList();
            var challenges = new List<IChallengeContext>();

            foreach (var authorization in authorizations)
            {
                var challenge = await authorization.Http();

                AcmeChallengeStore.Add(challenge.Token, challenge.KeyAuthz);
                challenges.Add(challenge);
            }

            foreach (var challenge in challenges)
                await challenge.Validate();

            var deadline = DateTimeOffset.UtcNow + ValidationTimeout;

            foreach (var authorization in authorizations)
            {
                while (true)
                {
                    var resource = await authorization.Resource();

                    if (resource.Status == AuthorizationStatus.Valid)
                        break;

                    if (resource.Status == AuthorizationStatus.Invalid)
                        throw new InvalidOperationException(Refused(resource));

                    if (DateTimeOffset.UtcNow > deadline)
                        throw new TimeoutException(
                            $"The ACME server did not finish checking {resource.Identifier?.Value} within {ValidationTimeout.TotalMinutes:0} minutes. " +
                            "Check that this hostname resolves to this server and that port 80 reaches it from the internet.");

                    await Task.Delay(PollInterval);
                }
            }
        }

        /// <summary>Why the ACME server refused one hostname, as it explained it.</summary>
        private static string Refused(Authorization authorization)
        {
            var detail = authorization.Challenges?
                .Select(challenge => challenge.Error?.Detail)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

            return $"{authorization.Identifier?.Value}: {detail ?? "the ACME server could not verify this hostname."}";
        }

        /// <summary>
        /// The ACME account, from the stored account key, generating and storing one the first time
        /// (D98). Registering with a key the server already knows returns the existing account, so
        /// this is safe to do on every order and there is no "have we registered?" flag to keep.
        /// </summary>
        private async Task<AcmeContext> Account(Uri directory, string email)
        {
            var stored = this.settings.Get(SettingsKeys.CertAcmeAccountKeyPem);
            var key = string.IsNullOrWhiteSpace(stored) ? null : KeyFactory.FromPem(stored);
            var context = new AcmeContext(directory, key);

            await context.NewAccount(email, termsOfServiceAgreed: true);

            if (key == null)
            {
                this.settings.Set(SettingsKeys.CertAcmeAccountKeyPem, context.AccountKey.ToPem());
                Log.Info($"ACME account registered at {directory} and its key stored");
            }

            return context;
        }

        /// <summary>
        /// Records why an order did not happen and hands the row back. The certificate, chain and
        /// key are untouched: a renewal that fails must leave the working certificate alone.
        /// </summary>
        private Certificate Failed(Certificate certificate, string message)
        {
            certificate.LastError = message;
            this.certificates.Update(certificate);

            Log.Warn($"Certificate '{certificate.Name}' order failed: {message}");
            return certificate;
        }
    }
}
