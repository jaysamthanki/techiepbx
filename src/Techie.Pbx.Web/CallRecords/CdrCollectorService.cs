using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.CallRecords
{
    /// <summary>
    /// Runs the <see cref="CdrCollector"/> for as long as the app runs (F5). A hosted service for
    /// the reason <c>CertificateRenewalService</c> is one: it has to happen whether or not anybody
    /// is signed in, and hosting a background worker is something ASP.NET Core does for us (D8).
    /// Everything it uses is built with <c>new</c>.
    ///
    /// The collector blocks on a socket read, so it gets a thread of its own rather than one from
    /// the pool, and stopping the app closes the socket under it.
    /// </summary>
    public class CdrCollectorService : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var settings = new SettingsRepository(PbxDatabase.Current);

            // Asterisk and this app are on the same box, so the zone Asterisk writes its CDR times
            // in is this process's local zone.
            var collector = new CdrCollector(
                new CdrRepository(PbxDatabase.Current),
                () => AsteriskSettings.Ami(settings.GetAll()),
                TimeZoneInfo.Local);

            return Task.Factory.StartNew(
                () => collector.Run(stoppingToken),
                stoppingToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }
}
