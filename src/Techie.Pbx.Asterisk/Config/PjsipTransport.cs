using System.Net;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// SIP transport settings. When the server sits behind 1:1 NAT (cloud VMs), set
    /// ExternalAddress to the public IP and LocalNets to the private subnet(s).
    /// </summary>
    public class PjsipTransport
    {
        /// <summary>The SIP port, which a trunk's server URI only mentions when it differs.</summary>
        public const int DefaultPort = 5060;

        public string BindAddress { get; set; } = "0.0.0.0";
        public string? ExternalAddress { get; set; }
        public List<string> LocalNets { get; set; } = new();
        public int Port { get; set; } = DefaultPort;

        public List<string> Validate()
        {
            var errors = new List<string>();

            if (!IPAddress.TryParse(BindAddress, out _))
                errors.Add("Bind address must be an IP address.");

            if (Port is < 1 or > 65535)
                errors.Add("Port must be between 1 and 65535.");

            foreach (var net in LocalNets)
            {
                if (!IPNetwork.TryParse(net, out _))
                    errors.Add($"Local network '{net}' must be in CIDR form, e.g. 10.0.0.0/24.");
            }

            if (ExternalAddress != null && !IPAddress.TryParse(ExternalAddress, out _))
                errors.Add("External address must be an IP address.");

            if (ExternalAddress != null && LocalNets.Count == 0)
                errors.Add("Local networks are required when an external address is set.");

            return errors;
        }
    }
}
