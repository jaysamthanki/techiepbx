using log4net;
using Techie.Pbx.Asterisk.Ami;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// Tells a Yealink phone, over AMI, to fetch its config again or to reboot — the Yealink
    /// equivalent of <see cref="PolycomPusher"/>, but a SIP NOTIFY through Asterisk rather than an
    /// HTTP call straight to the phone, because a Yealink phone has no web endpoint to push to
    /// (D91). Synchronous, like the rest of this codebase's AMI layer (see
    /// <c>ConfigApplier.Apply()</c>) rather than fire-and-forget on a background task.
    ///
    /// Best-effort by design, the same as the Polycom push: a phone that is off, or not
    /// registered, still gets the config at its next poll regardless, so a failed notify is a
    /// warning, never an exception a caller has to handle.
    /// </summary>
    public static class YealinkNotifier
    {
        /// <summary>The pjsip_notify.conf category that makes a phone re-fetch its config.</summary>
        public const string CheckConfigNotification = "tnpbx-check-cfg";

        /// <summary>The pjsip_notify.conf category that makes a phone reboot.</summary>
        public const string RebootNotification = "tnpbx-reboot";

        private static readonly ILog Log = LogManager.GetLogger(typeof(YealinkNotifier));

        public static bool NotifyCheckConfig(AmiSettings ami, string endpoint) => Notify(ami, endpoint, CheckConfigNotification);

        public static bool NotifyReboot(AmiSettings ami, string endpoint) => Notify(ami, endpoint, RebootNotification);

        private static bool Notify(AmiSettings ami, string endpoint, string notificationName)
        {
            try
            {
                using var client = new AmiClient(ami);
                var session = client.Connect();
                session.SendNotify(endpoint, notificationName);
                return true;
            }
            catch (Exception ex) when (ex is AmiException or IOException)
            {
                Log.Warn($"NOTIFY '{notificationName}' to endpoint '{endpoint}' failed: {ex.Message}");
                return false;
            }
        }
    }
}
