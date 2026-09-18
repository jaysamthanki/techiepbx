using System.Text;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// notify.conf: the two NOTIFY categories a Yealink phone is sent over AMI (D91) —
    /// <c>tnpbx-check-cfg</c> re-fetches its config, <c>tnpbx-reboot</c> reboots it. Real Yealink
    /// NOTIFY semantics, well established: <c>Event: check-sync</c> alone re-fetches config,
    /// <c>Event: check-sync;reboot=true</c> reboots.
    ///
    /// Fully static content, like <see cref="LoggerConfRenderer"/>: nothing here depends on the
    /// database, so this file never changes once written.
    /// </summary>
    public static class NotifyConfRenderer
    {
        public static string Render()
        {
            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[tnpbx-check-cfg]\n");
            sb.Append("Event = check-sync\n");

            sb.Append('\n');
            sb.Append("[tnpbx-reboot]\n");
            sb.Append("Event = check-sync;reboot=true\n");

            return sb.ToString();
        }
    }
}
