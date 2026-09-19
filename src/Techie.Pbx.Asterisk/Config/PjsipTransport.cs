using System.Net;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// The SIP and media settings, as the Sip.* keys of the Settings table give them. When the
    /// server sits behind 1:1 NAT (cloud VMs), set ExternalAddress to the public IP and LocalNets
    /// to the private subnet(s).
    ///
    /// Everything optional here is genuinely off when it is unset: no TCP port means no TCP
    /// transport is rendered at all (D70), no TLS port means no TLS transport (D101), and no STUN
    /// server means rtp.conf says nothing about STUN or ICE (D72).
    /// </summary>
    public class PjsipTransport
    {
        /// <summary>The SIP port, which a trunk's server URI only mentions when it differs.</summary>
        public const int DefaultPort = 5060;

        /// <summary>
        /// Where SIP over TLS listens when nobody has chosen a port. 5061 is the registered one, and
        /// unlike the TCP port an unset TLS port is not "off": the certificate is what decides
        /// whether the transport exists at all (D101).
        /// </summary>
        public const int DefaultTlsPort = 5061;

        public string BindAddress { get; set; } = "0.0.0.0";

        /// <summary>The codecs extensions are offered, in preference order (D73).</summary>
        public List<string> Codecs { get; set; } = SipCodecs.Parse(SipCodecs.Default);

        public string? ExternalAddress { get; set; }
        public List<string> LocalNets { get; set; } = new();
        public int Port { get; set; } = DefaultPort;

        /// <summary>The STUN server as host or host:port, or null for no STUN at all (D72).</summary>
        public string? StunServer { get; set; }

        /// <summary>The TCP SIP port, or null for no TCP transport at all (D70).</summary>
        public int? TcpPort { get; set; }

        /// <summary>
        /// The TLS SIP port, or null for no TLS transport at all. A port on its own is not enough:
        /// a TLS transport needs a certificate to name, and without one none is rendered (D101).
        /// </summary>
        public int? TlsPort { get; set; }

        /// <summary>Whether media should be offered ICE, which is what a STUN address is for.</summary>
        public bool UsesIce => this.StunServer != null || this.ExternalAddress != null;

        public List<string> Validate()
        {
            var errors = new List<string>();

            if (!IPAddress.TryParse(BindAddress, out _))
                errors.Add("Bind address must be an IP address.");

            if (Port is < 1 or > 65535)
                errors.Add("Port must be between 1 and 65535.");

            if (TcpPort is < 1 or > 65535)
                errors.Add("TCP port must be between 1 and 65535.");

            if (TlsPort is < 1 or > 65535)
                errors.Add("TLS port must be between 1 and 65535.");

            foreach (var net in LocalNets)
            {
                if (!IPNetwork.TryParse(net, out _))
                    errors.Add($"Local network '{net}' must be in CIDR form, e.g. 10.0.0.0/24.");
            }

            if (ExternalAddress != null && !IPAddress.TryParse(ExternalAddress, out _))
                errors.Add("External address must be an IP address.");

            if (ExternalAddress != null && LocalNets.Count == 0)
                errors.Add("Local networks are required when an external address is set.");

            if (StunServer != null && !SettingsValidation.IsStunServer(StunServer))
                errors.Add("STUN server must be a hostname or IP address, optionally with :port.");

            if (Codecs.Count == 0)
                errors.Add("At least one codec is required.");

            foreach (var codec in Codecs.Where(c => !SipCodecs.IsAllowed(c)))
                errors.Add($"Codec '{codec}' has no module on the allowlist.");

            return errors;
        }
    }
}
