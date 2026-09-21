using System.Text;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// pjsip_notify.conf: the NOTIFY messages a desk phone can be sent through Asterisk (D91,
    /// D123). A phone that has registered has told us where it is, so a NOTIFY reaches it wherever
    /// it is sitting — behind a home router, on a mobile hotspot — which an HTTP push to its web
    /// UI cannot (D118).
    ///
    /// Two types, one per brand, because <c>check-sync</c> means something slightly different to
    /// each: a Polycom phone reboots on a bare one, and a Yealink phone takes a
    /// <c>reboot=</c> parameter and re-reads its configuration without rebooting when it is false.
    /// Both carry <c>Content-Length: 0</c>, because the NOTIFY has no body and a phone that is sent
    /// one without that header is entitled to wait for a body that never comes.
    ///
    /// <b>No <c>[general]</c> section.</b> Asterisk 22 refuses the file outright with one — found
    /// on the lab VM, the same way D91 found that this file is <c>pjsip_notify.conf</c> and not the
    /// <c>notify.conf</c> that belonged to the dead chan_sip.
    ///
    /// Fully static content, like <see cref="LoggerConfRenderer"/>: nothing here depends on the
    /// database, so this file never changes once written. The app sends these NOTIFYs by AMI with
    /// the same headers spelled out (<c>PhoneNotifier</c>); the types here are what the equivalent
    /// <c>pjsip send notify polycom-reboot endpoint 1001</c> at the CLI uses.
    /// </summary>
    public static class NotifyConfRenderer
    {
        /// <summary>The type that reboots a Polycom phone.</summary>
        public const string PolycomReboot = "polycom-reboot";

        /// <summary>The type that makes a Yealink phone re-read its configuration.</summary>
        public const string YealinkReboot = "yealink-reboot";

        public static string Render()
        {
            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append($"[{PolycomReboot}]\n");
            sb.Append("Event = check-sync\n");
            sb.Append("Content-Length = 0\n");

            sb.Append('\n');
            sb.Append($"[{YealinkReboot}]\n");
            sb.Append("Event = check-sync;reboot=false\n");
            sb.Append("Content-Length = 0\n");

            return sb.ToString();
        }
    }
}
