using System.Text;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders rtp.conf: the UDP port range media uses. The firewall has to allow exactly this
    /// range (piece 19), so the numbers are public constants rather than text in a template.
    /// </summary>
    public static class RtpConfRenderer
    {
        public const int PortEnd = 20000;
        public const int PortStart = 10000;

        public static string Render()
        {
            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[general]\n");
            sb.Append($"rtpstart = {PortStart}\n");
            sb.Append($"rtpend = {PortEnd}\n");

            return sb.ToString();
        }
    }
}
