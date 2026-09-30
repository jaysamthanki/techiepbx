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

        /// <summary>
        /// This server's own interface address behind 1:1 NAT, or null. With ExternalAddress it
        /// maps the ICE host candidate from private to public in rtp.conf (D167).
        /// </summary>
        public string? LocalAddress { get; set; }

        public List<string> LocalNets { get; set; } = new();

        /// <summary>
        /// How many devices may register to one extension at once (D110): an office phone and
        /// a laptop softphone on the same number is 2. A call rings every registered contact,
        /// which is how PJSIP dials an endpoint. Default 1: a second registration replaces the
        /// first. One value for the whole system — per-extension is a later piece if ever wanted.
        /// </summary>
        public int MaxContacts { get; set; } = 1;
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

        /// <summary>
        /// Whether rtp.conf maps the local address to the external one as an ICE host candidate,
        /// which needs both halves of the mapping (D167).
        /// </summary>
        public bool UsesIceHostMapping => this.ExternalAddress != null && this.LocalAddress != null;

        public List<string> Validate()
        {
            var errors = new List<string>();

            if (!IPAddress.TryParse(this.BindAddress, out _))
                errors.Add("Bind address must be an IP address.");

            if (this.Port is < 1 or > 65535)
                errors.Add("Port must be between 1 and 65535.");

            if (this.MaxContacts is < 1 or > 5)
                errors.Add("Max contacts must be between 1 and 5.");

            if (this.TcpPort is < 1 or > 65535)
                errors.Add("TCP port must be between 1 and 65535.");

            if (this.TlsPort is < 1 or > 65535)
                errors.Add("TLS port must be between 1 and 65535.");

            foreach (var net in this.LocalNets)
            {
                if (!IPNetwork.TryParse(net, out _))
                    errors.Add($"Local network '{net}' must be in CIDR form, e.g. 10.0.0.0/24.");
            }

            if (this.ExternalAddress != null && !IPAddress.TryParse(this.ExternalAddress, out _))
                errors.Add("External address must be an IP address.");

            if (this.LocalAddress != null && !IPAddress.TryParse(this.LocalAddress, out _))
                errors.Add("Local address must be an IP address.");

            if (this.ExternalAddress != null && this.LocalNets.Count == 0)
                errors.Add("Local networks are required when an external address is set.");

            if (this.StunServer != null && !SettingsValidation.IsStunServer(this.StunServer))
                errors.Add("STUN server must be a hostname or IP address, optionally with :port.");

            if (this.Codecs.Count == 0)
                errors.Add("At least one codec is required.");

            foreach (var codec in this.Codecs.Where(c => !SipCodecs.IsAllowed(c)))
                errors.Add($"Codec '{codec}' has no module on the allowlist.");

            return errors;
        }
    }
}
