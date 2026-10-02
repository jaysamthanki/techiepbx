using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The web client's Asterisk side (D159): a web-enabled extension gets a second
    /// <c>-web</c> device in pjsip.conf, and every Dial that rings the extension — direct,
    /// ring group or as a forwarding target — rings both devices in the one Dial it always
    /// was. An extension without the web client renders exactly what it did before.
    /// </summary>
    public class WebClientRendererTests
    {
        private static Extension WebExtension() => new()
        {
            Number = "101",
            Name = "Front Desk",
            Secret = "AAAAbbbbCCCCdddd1111",
            WebClient = true,
        };

        private static List<Extension> Sample() => new()
        {
            WebExtension(),
            new Extension { Number = "102", Name = "Sales", Secret = "EEEEffffGGGGhhhh2222" },
        };

        [Fact]
        public void A_web_enabled_extension_gets_a_second_webrtc_endpoint()
        {
            var actual = PjsipConfRenderer.Render(new PjsipTransport(), Sample());

            Assert.Contains(
                "[101-web]\n" +
                "type = endpoint\n" +
                "webrtc = yes\n" +
                "context = internal\n" +
                "disallow = all\n" +
                "allow = ulaw,alaw\n" +
                "auth = 101-web-auth\n" +
                "aors = 101-web\n" +
                "callerid = \"Front Desk\" <101>\n",
                actual);
        }

        /// <summary>The PoC decision: the browser signs in with the extension's own secret.</summary>
        [Fact]
        public void The_web_device_shares_the_extensions_secret()
        {
            var actual = PjsipConfRenderer.Render(new PjsipTransport(), Sample());

            Assert.Contains(
                "[101-web-auth]\n" +
                "type = auth\n" +
                "auth_type = digest\n" +
                "username = 101-web\n" +
                "password = AAAAbbbbCCCCdddd1111\n",
                actual);
        }

        /// <summary>
        /// One web client per extension: remove_existing = yes whatever MaxContacts says, so a
        /// re-opened tab replaces the contact a closed one left behind.
        /// </summary>
        [Fact]
        public void The_web_aor_replaces_a_stale_contact_even_on_a_multi_device_system()
        {
            var actual = PjsipConfRenderer.Render(new PjsipTransport { MaxContacts = 2 }, Sample());

            Assert.Contains(
                "[101-web]\n" +
                "type = aor\n" +
                "max_contacts = 2\n" +
                "remove_existing = yes\n" +
                "qualify_frequency = 60\n",
                actual);

            // The base aor is untouched by the web client: a phone plus a softphone still coexist.
            Assert.Contains("[101]\ntype = aor\nmax_contacts = 2\nremove_existing = no\n", actual);
        }

        /// <summary>
        /// The web endpoint carries the same mailbox and the same outbound caller ID claim as
        /// the base one: the browser is the same person on the same number (D108, D125).
        /// </summary>
        [Fact]
        public void The_web_endpoint_shares_the_mailbox_and_the_caller_id_claim()
        {
            var extension = WebExtension();
            extension.OutboundCallerID = "17141234567";
            extension.VoicemailEnabled = true;
            extension.VoicemailPin = "4321";

            var actual = PjsipConfRenderer.Render(new PjsipTransport(), new List<Extension> { extension });
            var webEndpoint = actual[actual.IndexOf("[101-web]", StringComparison.Ordinal)..];

            Assert.Contains($"set_var = {PjsipConfRenderer.OutboundCallerIDVariable}=17141234567\n", webEndpoint);
            Assert.Contains($"mailboxes = 101@{VoicemailConfRenderer.MailboxContext}\n", webEndpoint);
        }

        [Fact]
        public void An_extension_without_the_web_client_gets_no_web_sections()
        {
            var extensions = new List<Extension>
            {
                new() { Number = "101", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
            };

            Assert.DoesNotContain(PjsipConfRenderer.WebClientSuffix, PjsipConfRenderer.Render(new PjsipTransport(), extensions));
            Assert.DoesNotContain(PjsipConfRenderer.WebClientSuffix, ExtensionsConfRenderer.Render(extensions));
        }

        /// <summary>
        /// The one Dial rings both devices, and everything behind it — ring time, options and
        /// the voicemail fallthrough — is exactly what it was: it is still one Dial.
        /// </summary>
        [Fact]
        public void Dialling_the_extension_rings_both_devices_in_one_dial()
        {
            var actual = ExtensionsConfRenderer.Render(Sample());

            Assert.Contains("exten => 101,1,Dial(PJSIP/101&PJSIP/101-web,30,tTkKr)\n same => n,Hangup()\n", actual);
            Assert.Contains("exten => 102,1,Dial(PJSIP/102,30,tTkKr)\n", actual);
        }

        /// <summary>The BLF lamp keeps watching the handset alone, as it does for forwarding (D121).</summary>
        [Fact]
        public void The_hint_still_watches_the_phone_only()
        {
            Assert.Contains("exten => 101,hint,PJSIP/101\n", ExtensionsConfRenderer.Render(Sample()));
        }

        [Fact]
        public void A_ring_group_member_with_the_web_client_rings_both_devices()
        {
            var group = new RingGroup
            {
                RingGroupID = 1,
                Number = "600",
                Name = "Support",
                Strategy = "All",
                Members = "101,102",
                RingSeconds = 20,
            };

            var actual = ExtensionsConfRenderer.Render(
                Sample(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                new List<RingGroup> { group });

            Assert.Contains(" same => n,Dial(PJSIP/101&PJSIP/101-web&PJSIP/102,20,tTkKr)\n", actual);
        }

        /// <summary>
        /// A forwarding target that is a web-enabled extension rings that extension's web client
        /// too: the fork lives where the device string is built, so every path gets it (D130, D159).
        /// </summary>
        [Fact]
        public void A_forwarding_target_with_the_web_client_rings_both_devices()
        {
            var extensions = Sample();
            extensions[1].Forwarding = "101";

            var actual = ExtensionsConfRenderer.Render(extensions);

            Assert.Contains("exten => 102,1,Dial(PJSIP/101&PJSIP/101-web,30,tTkKr)\n", actual);
        }
    }
}
