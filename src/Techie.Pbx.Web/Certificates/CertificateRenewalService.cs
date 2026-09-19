using log4net;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Certificates
{
    /// <summary>
    /// Renews certificates before they expire (D100). Once a day it looks at every row and orders
    /// again for any that is inside <see cref="Certificate.RenewalThresholdDays"/> days of expiry,
    /// or that has never been issued at all because its first order failed.
    ///
    /// A hosted service rather than a cron entry or a timer somewhere in a page: it is the one
    /// thing in this app that has to happen without anybody being signed in, and hosting a
    /// background worker is something ASP.NET Core does for us (D8 — the framework is what forces
    /// the registration; it builds its own repositories with <c>new</c> like everything else).
    ///
    /// Renewal keeps the row, so the certificate a renewal replaces is gone as soon as the new one
    /// is stored. What it does not do is reach the running web server: Kestrel loads its
    /// certificate at startup (D99), so a renewed certificate serves SIP at the next Asterisk
    /// restart and the browser at the next app restart.
    /// </summary>
    public class CertificateRenewalService : BackgroundService
    {
        /// <summary>How long after startup the first check runs. Long enough to be out of the way
        /// of everything else starting, short enough that an overdue certificate is not waiting a
        /// day for its first attempt.</summary>
        public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(5);

        /// <summary>How often the rows are looked at. Daily is ample against a 30 day threshold.</summary>
        public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

        private static readonly ILog Log = LogManager.GetLogger(typeof(CertificateRenewalService));

        /// <summary>
        /// The rows that should be ordered again now. Pure, so the rule that decides whether a
        /// site's certificates are renewed in time can be tested without a network.
        /// </summary>
        public static List<Certificate> DueForRenewal(IEnumerable<Certificate> certificates, DateTimeOffset now) =>
            certificates.Where(certificate => certificate.NeedsRenewal(now)).ToList();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(FirstCheckDelay, stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await this.Check();
                    await Task.Delay(Interval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // The app is stopping, which is not a renewal failure.
            }
        }

        /// <summary>
        /// One pass. Every row is ordered separately and a failure on one is logged and left in
        /// that row's LastError, because one hostname that no longer points here must not stop the
        /// rest of a site's certificates being renewed.
        /// </summary>
        private async Task Check()
        {
            var repository = new CertificateRepository(PbxDatabase.Current);
            var due = DueForRenewal(repository.GetAll(), DateTimeOffset.UtcNow);

            if (due.Count == 0)
            {
                Log.Debug("Certificate renewal: nothing is due");
                return;
            }

            var service = new AcmeCertificateService(PbxDatabase.Current);

            foreach (var certificate in due)
            {
                Log.Info($"Certificate renewal: '{certificate.Name}' is due ({Due(certificate)})");

                var result = await service.Order(certificate);

                if (result.LastError != null)
                    Log.Warn($"Certificate renewal: '{certificate.Name}' failed, will try again tomorrow: {result.LastError}");
            }
        }

        /// <summary>Why a row is being renewed, for the log line that says so.</summary>
        private static string Due(Certificate certificate) =>
            certificate.DaysUntilExpiry(DateTimeOffset.UtcNow) is { } days
                ? $"{days} days left"
                : "never issued";
    }
}
