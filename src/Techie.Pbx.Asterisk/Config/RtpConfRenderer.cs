using System.Text;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders rtp.conf: the UDP port range media uses, and — when the settings ask for it — the
    /// STUN server and ICE (D72). The firewall has to allow exactly this range (piece 19), so the
    /// numbers are public constants rather than text in a template.
    ///
    /// rtp.conf is read at startup, so a change here is a restart, not a reload (D33).
    /// </summary>
    public static class RtpConfRenderer
    {
        public const int PortEnd = 20000;
        public const int PortStart = 10000;

        public static string Render() => Render(new PjsipTransport());

        public static string Render(PjsipTransport transport)
        {
            var errors = transport.Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException("Invalid transport: " + string.Join(" ", errors));

            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[general]\n");
            sb.Append($"rtpstart = {PortStart}\n");
            sb.Append($"rtpend = {PortEnd}\n");

            // Asterisk 22's res_rtp_asterisk takes stunaddr as host or host:port and re-resolves
            // it periodically; verified on the lab VM before this was written (D72).
            if (transport.StunServer != null)
                sb.Append($"stunaddr = {ConfText.Safe(transport.StunServer, "STUN server")}\n");

            // ICE is only worth its candidates when something knows what the outside looks like.
            if (transport.UsesIce)
                sb.Append("icesupport = yes\n");

            return sb.ToString();
        }
    }
}
