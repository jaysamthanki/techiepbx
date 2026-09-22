using log4net;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// Keeps one AMI connection open for as long as the app runs and stores every Cdr event that
    /// arrives on it (F5). Unlike every other AMI conversation here, which connects, asks and hangs
    /// up, this one only listens: cdr_manager pushes a record when a call ends, and a record nobody
    /// was connected to hear is gone.
    ///
    /// Nothing on the event side is allowed to end the connection. A record that cannot be read or
    /// stored is logged and skipped. A connection that drops — an Asterisk restart, a reload of
    /// manager.conf (D34) — is opened again after a pause that grows while it keeps failing.
    /// </summary>
    public class CdrCollector
    {
        /// <summary>The longest pause between attempts to reconnect.</summary>
        public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

        /// <summary>The first pause after a connection drops or fails.</summary>
        public static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(5);

        private static readonly ILog Log = LogManager.GetLogger(typeof(CdrCollector));

        private readonly CdrRepository cdrs;
        private readonly Func<AmiSettings> settings;
        private readonly TimeZoneInfo serverZone;

        /// <param name="settings">
        /// Read again before every connection, so a changed AMI password is picked up by the next
        /// reconnect rather than the next app restart.
        /// </param>
        /// <param name="serverZone">The zone Asterisk writes its CDR times in: this server's own.</param>
        public CdrCollector(CdrRepository cdrs, Func<AmiSettings> settings, TimeZoneInfo serverZone)
        {
            this.cdrs = cdrs;
            this.settings = settings;
            this.serverZone = serverZone;
        }

        /// <summary>
        /// Reads events from an already logged-in session until it closes or
        /// <paramref name="stopping"/> is cancelled, storing each call record.
        /// </summary>
        public void Collect(AmiSession session, CancellationToken stopping)
        {
            session.ReadEvents(message =>
            {
                if (CdrEvent.Is(message))
                    this.Store(message);

                return !stopping.IsCancellationRequested;
            });
        }

        /// <summary>
        /// The pause before the next attempt: doubling from <see cref="MinRetryDelay"/> for each
        /// failure in a row, up to <see cref="MaxRetryDelay"/>.
        /// </summary>
        public static TimeSpan RetryDelay(int failures)
        {
            var seconds = MinRetryDelay.TotalSeconds * Math.Pow(2, Math.Clamp(failures - 1, 0, 10));
            return TimeSpan.FromSeconds(Math.Min(seconds, MaxRetryDelay.TotalSeconds));
        }

        /// <summary>
        /// Connects, collects, and connects again whenever the connection goes, until
        /// <paramref name="stopping"/> is cancelled. Blocks the calling thread throughout, so run it
        /// on one of its own.
        /// </summary>
        public void Run(CancellationToken stopping)
        {
            var failures = 0;

            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    var ami = this.settings();

                    // Nothing may arrive for hours on a quiet system, and that is not a dead
                    // connection. Asterisk closes the socket when it stops, which ends the read.
                    ami.TimeoutSeconds = 0;

                    using var client = new AmiClient(ami);

                    // Closing the socket is what gets a blocked read to return when the app stops.
                    using var closeOnStop = stopping.Register(client.Dispose);

                    var session = client.Connect();

                    // Stopped while connecting: the close above may have happened before there
                    // was a socket to close.
                    if (stopping.IsCancellationRequested)
                        return;

                    Log.Info("CDR collector: listening for call records");
                    this.Collect(session, stopping);

                    if (!stopping.IsCancellationRequested)
                        Log.Warn("CDR collector: the AMI connection closed; reconnecting");

                    failures = 0;
                }
                catch (Exception ex) when (!stopping.IsCancellationRequested)
                {
                    failures++;

                    // Loud the first time, quiet while it stays down: a PBX without Asterisk
                    // running should not fill the log with the same line every minute.
                    var line = $"CDR collector: could not read from AMI, retrying in {RetryDelay(failures).TotalSeconds:0}s: {ex.Message}";
                    if (failures == 1)
                        Log.Error(line);
                    else
                        Log.Debug(line);
                }
                catch (Exception)
                {
                    // Stopping: the read failed because the socket was closed under it on purpose.
                    return;
                }

                stopping.WaitHandle.WaitOne(failures == 0 ? MinRetryDelay : RetryDelay(failures));
            }
        }

        /// <summary>
        /// Stores one record. Returns whether it was stored; a record that could not be read or
        /// written is logged and dropped, never thrown, because the connection matters more than
        /// any one record on it.
        /// </summary>
        public bool Store(AmiMessage message)
        {
            try
            {
                var cdr = CdrEvent.ToCdr(message, this.serverZone, DateTime.UtcNow);
                var stored = this.cdrs.Insert(cdr);

                if (stored)
                    Log.Debug($"CDR collector: stored {cdr.UniqueID}/{cdr.Sequence} ({cdr.Disposition})");
                else
                    Log.Debug($"CDR collector: {cdr.UniqueID}/{cdr.Sequence} was already stored");

                return stored;
            }
            catch (Exception ex)
            {
                Log.Error($"CDR collector: dropped a call record that could not be stored: {ex.Message}");
                return false;
            }
        }
    }
}
