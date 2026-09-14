using System.Text;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders pjsip.conf from the transport settings and extensions. Pure function: no I/O.
    /// </summary>
    public static class PjsipConfRenderer
    {
        public static string Render(PjsipTransport transport, IEnumerable<Extension> extensions)
        {
            var transportErrors = transport.Validate();
            if (transportErrors.Count > 0)
                throw new InvalidOperationException("Invalid transport: " + string.Join(" ", transportErrors));

            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[transport-udp]\n");
            sb.Append("type = transport\n");
            sb.Append("protocol = udp\n");
            sb.Append($"bind = {transport.BindAddress}:{transport.Port}\n");
            foreach (var net in transport.LocalNets)
                sb.Append($"local_net = {ConfText.Safe(net, "local_net")}\n");
            if (transport.ExternalAddress != null)
            {
                sb.Append($"external_media_address = {transport.ExternalAddress}\n");
                sb.Append($"external_signaling_address = {transport.ExternalAddress}\n");
            }

            foreach (var extension in ConfText.EnabledInOrder(extensions))
            {
                var number = ConfText.Safe(extension.Number, "number");
                var name = ConfText.Safe(extension.Name, "name");
                var secret = ConfText.Safe(extension.Secret, "secret");

                sb.Append('\n');
                sb.Append($"[{number}]\n");
                sb.Append("type = endpoint\n");
                sb.Append($"context = {ExtensionsConfRenderer.InternalContext}\n");
                sb.Append("disallow = all\n");
                sb.Append("allow = ulaw,alaw\n");
                sb.Append($"auth = {number}-auth\n");
                sb.Append($"aors = {number}\n");
                sb.Append($"callerid = \"{name}\" <{number}>\n");
                sb.Append("direct_media = no\n");
                sb.Append("rtp_symmetric = yes\n");
                sb.Append("force_rport = yes\n");
                sb.Append("rewrite_contact = yes\n");

                sb.Append('\n');
                sb.Append($"[{number}-auth]\n");
                sb.Append("type = auth\n");
                sb.Append("auth_type = digest\n");
                sb.Append($"username = {number}\n");
                sb.Append($"password = {secret}\n");

                sb.Append('\n');
                sb.Append($"[{number}]\n");
                sb.Append("type = aor\n");
                sb.Append("max_contacts = 1\n");
                sb.Append("remove_existing = yes\n");
            }

            return sb.ToString();
        }
    }
}
