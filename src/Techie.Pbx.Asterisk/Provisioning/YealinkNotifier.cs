using System.Net.Sockets;
using log4net;
using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// Tells a desk phone, over AMI, to reboot or to fetch its config again: a SIP NOTIFY through
    /// Asterisk to the extension the phone registers as (D91, D123). The NOTIFY follows the
    /// registered contact, so it reaches a phone behind NAT — which the HTTP push to a Polycom
    /// phone's own web UI cannot (D118), and which a Yealink phone has never had at all.
    ///
    /// The headers are the ones pjsip_notify.conf spells out for the CLI
    /// (<see cref="Config.NotifyConfRenderer"/>). They differ by brand because <c>check-sync</c>
    /// does: a Polycom phone reboots on a bare one, and a Yealink phone reads the <c>reboot=</c>
    /// parameter and only reboots when it is true.
    ///
    /// Synchronous, like the rest of this codebase's AMI layer (see <c>ConfigApplier.Apply()</c>)
    /// rather than fire-and-forget on a background task. Best-effort by design: a phone that is off
    /// or not registered still gets its config at the next poll, so a failed notify is a warning,
    /// never an exception a caller has to handle.
    /// </summary>
    public static class PhoneNotifier
    {
        /// <summary>
        /// Every NOTIFY here has an empty body, and a phone sent one without this header is
        /// entitled to sit waiting for a body that never arrives.
        /// </summary>
        private static readonly (string Name, string Value) NoBody = ("Content-Length", "0");

        private static readonly (string Name, string Value)[] PolycomReboot =
            { ("Event", "check-sync"), NoBody };

        private static readonly (string Name, string Value)[] YealinkCheckConfig =
            { ("Event", "check-sync;reboot=false"), NoBody };

        private static readonly (string Name, string Value)[] YealinkReboot =
            { ("Event", "check-sync;reboot=true"), NoBody };

        private static readonly ILog Log = LogManager.GetLogger(typeof(PhoneNotifier));

        /// <summary>
        /// Tells the phone to re-read its configuration without rebooting. Only a Yealink phone
        /// can be asked that: <c>check-sync</c> on its own reboots a Polycom phone, which is why a
        /// Polycom one is pushed over its web UI when it is reachable instead (D86).
        /// </summary>
        public static bool NotifyCheckConfig(AmiSettings ami, string endpoint) =>
            Notify(ami, endpoint, YealinkCheckConfig);

        /// <summary>Tells the phone to reboot. Any call on it ends immediately.</summary>
        public static bool NotifyReboot(AmiSettings ami, string brand, string endpoint) =>
            Notify(ami, endpoint, string.Equals(brand, PhoneBrand.Yealink, StringComparison.Ordinal)
                ? YealinkReboot
                : PolycomReboot);

        private static bool Notify(AmiSettings ami, string endpoint, (string Name, string Value)[] headers)
        {
            try
            {
                using var client = new AmiClient(ami);
                var session = client.Connect();
                session.SendNotify(endpoint, headers);
                return true;
            }
            // SocketException as well as the other two: Asterisk being down is the ordinary way
            // this fails, and it must come back as a toast rather than as a 500 (as
            // RegistrationStatus.Read already has it).
            catch (Exception ex) when (ex is AmiException or IOException or SocketException)
            {
                Log.Warn($"NOTIFY to endpoint '{endpoint}' failed: {ex.Message}");
                return false;
            }
        }
    }
}
