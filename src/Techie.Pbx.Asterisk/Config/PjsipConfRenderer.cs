using System.Text;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders pjsip.conf from the transport settings, the extensions and the trunks. Pure
    /// function: no I/O. Trunks are appended after the extensions, so a system with no trunks
    /// generates exactly the file it generated before there were any (D39).
    /// </summary>
    public static class PjsipConfRenderer
    {
        /// <summary>
        /// What a trunk's registration section is called: the trunk name plus this. The AMI
        /// status lookup reads it back off the object name, so both ends use this constant.
        /// </summary>
        public const string RegistrationSuffix = "-reg";

        /// <summary>The transport every endpoint and registration is bound to.</summary>
        public const string TransportName = "transport-udp";

        /// <summary>
        /// The TCP transport, which only exists in the file when a TCP port is set (D70). Nothing
        /// is bound to it by name: a phone that connects over TCP is matched by the endpoint it
        /// authenticates as, and trunks stay on UDP.
        /// </summary>
        public const string TcpTransportName = "transport-tcp";

        /// <summary>
        /// The TLS transport, which only exists in the file when there is a usable certificate to
        /// point it at (D101). Like the TCP one, nothing is bound to it by name.
        /// </summary>
        public const string TlsTransportName = "transport-tls";

        /// <summary>
        /// The one file Asterisk is given for TLS: certificate, issuers and private key together
        /// (D101). Written into the conf directory by <see cref="ConfigApplier"/> from the
        /// certificate row, so there is nothing for an operator to copy into place.
        /// </summary>
        public const string TlsCertificateFileName = "tnpbx-cert.pem";

        /// <summary>
        /// The lowest TLS version the transport will speak. <b>To confirm on the lab VM</b>: pjsip
        /// documents <c>method</c> as the SSL method, and whether <c>tlsv1_2</c> means "1.2 and
        /// later" or "1.2 exactly" decides whether a TLS 1.3 handset can connect. If it turns out
        /// to pin the version, this is one constant.
        /// </summary>
        private const string TlsMethod = "tlsv1_2";

        /// <summary>How long a registration lasts before it is renewed, in seconds.</summary>
        private const int RegistrationExpiration = 3600;

        /// <summary>How long to wait before trying a failed registration again, in seconds.</summary>
        private const int RegistrationRetryInterval = 60;

        /// <summary>How often a trunk we register to is qualified (OPTIONS ping), in seconds.</summary>
        private const int TrunkQualifyFrequency = 60;

        public static string Render(PjsipTransport transport, IEnumerable<Extension> extensions) =>
            Render(transport, extensions, new List<Trunk>());

        public static string Render(PjsipTransport transport, IEnumerable<Extension> extensions, IEnumerable<Trunk> trunks) =>
            Render(transport, extensions, trunks, null, AsteriskSettings.DefaultConfDirectory);

        /// <summary>
        /// The whole file. The certificate is an input like the extensions and the trunks are: when
        /// there is a usable one, a TLS transport is rendered pointing at the combined PEM this
        /// system writes beside the other generated files; when there is not, the file is exactly
        /// what it was before certificates existed (D101).
        /// </summary>
        public static string Render(
            PjsipTransport transport,
            IEnumerable<Extension> extensions,
            IEnumerable<Trunk> trunks,
            Certificate? certificate,
            string confDirectory)
        {
            var transportErrors = transport.Validate();
            if (transportErrors.Count > 0)
                throw new InvalidOperationException("Invalid transport: " + string.Join(" ", transportErrors));

            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            AppendTransport(sb, transport, TransportName, "udp", transport.Port);

            // Only when a TCP port is set: an unset port means no TCP listener at all (D70).
            if (transport.TcpPort != null)
                AppendTransport(sb, transport, TcpTransportName, "tcp", transport.TcpPort.Value);

            // Only with a certificate to present. A TLS transport without cert_file stops
            // res_pjsip loading the file at all, which is what kept this out until now (D71, D101).
            if (certificate != null)
                AppendTlsTransport(sb, transport, TlsCertificatePath(confDirectory));

            var codecs = string.Join(",", transport.Codecs.Select(c => ConfText.Safe(c, "codec")));

            foreach (var extension in ConfText.EnabledInOrder(extensions))
            {
                if (extension.MaxContacts is < 1 or > 5)
                    throw new InvalidOperationException($"Extension {extension.Number} has an invalid max contacts value.");

                var number = ConfText.Safe(extension.Number, "number");
                var name = ConfText.Safe(extension.Name, "name");
                var secret = ConfText.Safe(extension.Secret, "secret");

                sb.Append('\n');
                sb.Append($"[{number}]\n");
                sb.Append("type = endpoint\n");
                sb.Append($"context = {ExtensionsConfRenderer.InternalContext}\n");
                sb.Append("disallow = all\n");
                sb.Append($"allow = {codecs}\n");
                sb.Append($"auth = {number}-auth\n");
                sb.Append($"aors = {number}\n");
                sb.Append($"callerid = \"{name}\" <{number}>\n");
                sb.Append("direct_media = no\n");
                sb.Append("rtp_symmetric = yes\n");
                sb.Append("force_rport = yes\n");
                sb.Append("rewrite_contact = yes\n");

                // The mailbox this endpoint's MWI light reports: with it, res_pjsip_mwi answers the
                // phone's SUBSCRIBE and Asterisk sends a NOTIFY the moment a message arrives or is
                // heard (D108). Only an endpoint with voicemail on has a mailbox to name.
                if (extension.VoicemailEnabled)
                    sb.Append($"mailboxes = {number}@{VoicemailConfRenderer.MailboxContext}\n");

                sb.Append('\n');
                sb.Append($"[{number}-auth]\n");
                sb.Append("type = auth\n");
                sb.Append("auth_type = digest\n");
                sb.Append($"username = {number}\n");
                sb.Append($"password = {secret}\n");

                sb.Append('\n');
                sb.Append($"[{number}]\n");
                sb.Append("type = aor\n");

                // How many devices may be registered at once (D110). With more than one,
                // remove_existing must be off: it deletes every other contact on a new
                // REGISTER, which is the opposite of an office phone plus a laptop softphone
                // coexisting. Stale contacts are still pruned by the registration expiring.
                sb.Append($"max_contacts = {extension.MaxContacts}\n");

                if (extension.MaxContacts > 1)
                    sb.Append("remove_existing = no\n");
                else
                    sb.Append("remove_existing = yes\n");
            }

            foreach (var trunk in TrunkRenderOrder(trunks))
                AppendTrunk(sb, trunk);

            return sb.ToString();
        }

        /// <summary>
        /// Where the combined PEM lives: beside the generated conf files, so one directory holds
        /// everything this system writes for Asterisk and one set of permissions covers it (D101).
        /// </summary>
        public static string TlsCertificatePath(string confDirectory) =>
            Path.Combine(confDirectory, TlsCertificateFileName);

        /// <summary>Enabled trunks by name, re-validated so a bad row cannot reach a conf file.</summary>
        public static List<Trunk> TrunkRenderOrder(IEnumerable<Trunk> trunks)
        {
            var enabled = trunks.Where(t => t.Enabled).ToList();

            foreach (var trunk in enabled)
            {
                var errors = trunk.Validate();
                if (errors.Count > 0)
                    throw new InvalidOperationException($"Trunk '{trunk.Name}' is invalid: {string.Join(" ", errors)}");
            }

            return enabled.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The TLS transport: an ordinary transport section plus the certificate. Both
        /// <c>cert_file</c> and <c>priv_key_file</c> name the same file, because what is written
        /// there is certificate, issuers and key in one (D101).
        /// </summary>
        private static void AppendTlsTransport(StringBuilder sb, PjsipTransport transport, string certificatePath)
        {
            AppendTransport(sb, transport, TlsTransportName, "tls", transport.TlsPort ?? PjsipTransport.DefaultTlsPort);

            var path = ConfText.Safe(certificatePath, "certificate path");

            sb.Append($"cert_file = {path}\n");
            sb.Append($"priv_key_file = {path}\n");
            sb.Append($"method = {TlsMethod}\n");
        }

        /// <summary>
        /// One transport section. UDP and TCP differ only in the protocol and the port: the same
        /// bind address, the same local networks and the same external addresses, because they
        /// describe the machine rather than the protocol (D70).
        /// </summary>
        private static void AppendTransport(StringBuilder sb, PjsipTransport transport, string name, string protocol, int port)
        {
            sb.Append('\n');
            sb.Append($"[{name}]\n");
            sb.Append("type = transport\n");
            sb.Append($"protocol = {protocol}\n");
            sb.Append($"bind = {transport.BindAddress}:{port}\n");

            foreach (var net in transport.LocalNets)
                sb.Append($"local_net = {ConfText.Safe(net, "local_net")}\n");

            if (transport.ExternalAddress != null)
            {
                sb.Append($"external_media_address = {transport.ExternalAddress}\n");
                sb.Append($"external_signaling_address = {transport.ExternalAddress}\n");
            }
        }

        /// <summary>
        /// One trunk, in the order the Asterisk and provider documentation writes it: registration,
        /// auth, aor, endpoint, identify (D39).
        /// </summary>
        private static void AppendTrunk(StringBuilder sb, Trunk trunk)
        {
            var name = ConfText.Safe(trunk.Name, "trunk name");
            var host = ConfText.Safe(trunk.ServerHost, "server host");
            var username = ConfText.Safe(trunk.Username, "username");
            var server = trunk.ServerPort == PjsipTransport.DefaultPort ? host : $"{host}:{trunk.ServerPort}";
            var hasAuth = trunk.Password.Length > 0;

            if (trunk.Register)
            {
                sb.Append('\n');
                sb.Append($"[{name}{RegistrationSuffix}]\n");
                sb.Append("type = registration\n");
                sb.Append($"transport = {TransportName}\n");
                sb.Append($"outbound_auth = {name}-auth\n");
                sb.Append($"server_uri = sip:{server}\n");
                sb.Append($"client_uri = sip:{username}@{server}\n");
                sb.Append($"contact_user = {username}\n");
                sb.Append($"retry_interval = {RegistrationRetryInterval}\n");
                sb.Append($"expiration = {RegistrationExpiration}\n");
            }

            if (hasAuth)
            {
                sb.Append('\n');
                sb.Append($"[{name}-auth]\n");
                sb.Append("type = auth\n");
                sb.Append("auth_type = userpass\n");
                sb.Append($"username = {ConfText.Safe(trunk.EffectiveAuthUsername, "auth username")}\n");
                sb.Append($"password = {ConfText.Safe(trunk.Password, "password")}\n");
            }

            sb.Append('\n');
            sb.Append($"[{name}]\n");
            sb.Append("type = aor\n");
            if (trunk.Register)
            {
                // Registering tells the provider where we are, so the contact comes from that.
                // Qualify watches whether the provider is still answering.
                sb.Append($"qualify_frequency = {TrunkQualifyFrequency}\n");
            }
            else
            {
                sb.Append($"contact = sip:{server}\n");
            }

            sb.Append('\n');
            sb.Append($"[{name}]\n");
            sb.Append("type = endpoint\n");
            sb.Append($"transport = {TransportName}\n");
            sb.Append($"context = {ConfText.Safe(trunk.Context, "trunk context")}\n");
            sb.Append("disallow = all\n");
            sb.Append($"allow = {string.Join(",", trunk.CodecList().Select(c => ConfText.Safe(c, "codec")))}\n");
            sb.Append($"aors = {name}\n");
            if (hasAuth)
                sb.Append($"outbound_auth = {name}-auth\n");
            sb.Append($"from_domain = {host}\n");
            if (username.Length > 0)
                sb.Append($"from_user = {username}\n");
            if (trunk.CallerIDNumber.Length > 0)
            {
                var callerIDName = ConfText.Safe(trunk.CallerIDName, "caller ID name");
                var callerIDNumber = ConfText.Safe(trunk.CallerIDNumber, "caller ID number");
                sb.Append($"callerid = \"{callerIDName}\" <{callerIDNumber}>\n");
            }

            sb.Append("direct_media = no\n");
            sb.Append("rtp_symmetric = yes\n");
            sb.Append("force_rport = yes\n");
            sb.Append("ice_support = no\n");
            sb.Append("send_rpid = yes\n");
            sb.Append("timers = no\n");

            var matches = trunk.MatchAddressList();
            if (matches.Count > 0)
            {
                sb.Append('\n');
                sb.Append($"[{name}-identify]\n");
                sb.Append("type = identify\n");
                sb.Append($"endpoint = {name}\n");
                foreach (var match in matches)
                    sb.Append($"match = {ConfText.Safe(match, "match address")}\n");
            }
        }
    }
}
