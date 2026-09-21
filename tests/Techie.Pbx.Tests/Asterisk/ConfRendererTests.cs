using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    public class ConfRendererTests
    {
        private static List<Extension> SampleExtensions() => new()
        {
            // Deliberately out of order, plus a disabled one that must not be rendered at all,
            // voicemail or no voicemail.
            new Extension
            {
                Number = "1002",
                Name = "O'Brien (Sales)",
                Secret = "EEEEffffGGGGhhhh2222",
                VoicemailEnabled = true,
                VoicemailPin = "4321",
                VoicemailEmail = "sales@example.com",
            },
            new Extension
            {
                Number = "1003",
                Name = "Disabled Phone",
                Secret = "IIIIjjjjKKKKllll3333",
                Enabled = false,
                VoicemailEnabled = true,
                VoicemailPin = "9999",
            },
            new Extension { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
        };

        private static List<Extension> WithoutVoicemail() => new()
        {
            new Extension { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
        };

        private static PjsipTransport NatTransport() => new()
        {
            LocalNets = { "10.8.20.0/24" },
            ExternalAddress = "203.0.113.10",
        };

        /// <summary>
        /// The same NAT transport with TCP switched on and gsm added to the codecs, which is what
        /// the two new SIP settings do (D70, D73). UDP and TCP share the port number on purpose:
        /// they are different sockets, and 5060 is what a phone tries for either.
        /// </summary>
        private static PjsipTransport TcpTransport()
        {
            var transport = NatTransport();

            transport.Codecs = new List<string> { "ulaw", "alaw", "gsm" };
            transport.TcpPort = 5060;

            return transport;
        }

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        [Fact]
        public void Pjsip_matches_expected_file()
        {
            var actual = PjsipConfRenderer.Render(NatTransport(), SampleExtensions());
            Assert.Equal(Expected("pjsip.conf"), actual);
        }

        [Fact]
        public void Pjsip_with_tcp_and_a_codec_list_matches_expected_file()
        {
            var actual = PjsipConfRenderer.Render(TcpTransport(), SampleExtensions());
            Assert.Equal(Expected("pjsip-tcp.conf"), actual);
        }

        /// <summary>
        /// No TCP port, no TCP transport: an unset port means the listener does not exist rather
        /// than falling back to a default one (D70).
        /// </summary>
        [Fact]
        public void Pjsip_has_no_tcp_transport_until_a_tcp_port_is_set()
        {
            var actual = PjsipConfRenderer.Render(new PjsipTransport(), SampleExtensions());

            Assert.DoesNotContain(PjsipConfRenderer.TcpTransportName, actual);
            Assert.DoesNotContain("protocol = tcp", actual);
        }

        /// <summary>The TCP bind comes from the TCP port setting, not from the UDP one.</summary>
        [Fact]
        public void The_tcp_transport_binds_the_tcp_port()
        {
            var transport = new PjsipTransport { TcpPort = 5062 };

            var actual = PjsipConfRenderer.Render(transport, SampleExtensions());

            Assert.Contains("[transport-udp]\ntype = transport\nprotocol = udp\nbind = 0.0.0.0:5060\n", actual);
            Assert.Contains("[transport-tcp]\ntype = transport\nprotocol = tcp\nbind = 0.0.0.0:5062\n", actual);
        }

        /// <summary>
        /// There is no TLS transport to render yet: without certificate management a TLS transport
        /// has no cert_file, and res_pjsip refuses to load the file at all (D71).
        /// </summary>
        [Fact]
        public void Pjsip_never_renders_a_tls_transport()
        {
            var actual = PjsipConfRenderer.Render(TcpTransport(), SampleExtensions());

            Assert.DoesNotContain("protocol = tls", actual);
            Assert.DoesNotContain("cert_file", actual);
        }

        /// <summary>
        /// The endpoints' codecs are the setting's, not a line baked into the renderer (D73).
        /// </summary>
        [Fact]
        public void Endpoint_codecs_come_from_the_settings()
        {
            var transport = new PjsipTransport { Codecs = new List<string> { "gsm" } };

            var actual = PjsipConfRenderer.Render(transport, SampleExtensions());

            Assert.Contains("disallow = all\nallow = gsm\n", actual);
            Assert.DoesNotContain("allow = ulaw,alaw\n", actual);
        }

        /// <summary>
        /// A codec with no module on the allowlist would render config Asterisk cannot honour, so
        /// the renderer refuses it even if it somehow got into the settings table (D73).
        /// </summary>
        [Fact]
        public void Pjsip_refuses_a_codec_that_has_no_module()
        {
            var transport = new PjsipTransport { Codecs = new List<string> { "opus" } };

            Assert.Throws<InvalidOperationException>(() => PjsipConfRenderer.Render(transport, SampleExtensions()));
        }

        [Fact]
        public void Extensions_matches_expected_file()
        {
            var actual = ExtensionsConfRenderer.Render(SampleExtensions());
            Assert.Equal(Expected("extensions.conf"), actual);
        }

        /// <summary>
        /// Every enabled extension gets a hint, which is what a BLF key on another phone
        /// subscribes to (D121). One per extension whether anything watches it or not: a key
        /// assigned later must not need an apply before its lamp works.
        /// </summary>
        [Fact]
        public void Every_enabled_extension_gets_a_hint()
        {
            var actual = ExtensionsConfRenderer.Render(SampleExtensions());

            Assert.Contains("exten => 1001,hint,PJSIP/1001\n", actual);
            Assert.Contains("exten => 1002,hint,PJSIP/1002\n", actual);
        }

        /// <summary>A switched-off extension has no endpoint, so there is nothing to watch.</summary>
        [Fact]
        public void A_disabled_extension_gets_no_hint()
        {
            var extensions = new List<Extension>
            {
                new() { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111", Enabled = false },
            };

            Assert.DoesNotContain("hint", ExtensionsConfRenderer.Render(extensions));
        }

        [Fact]
        public void Voicemail_matches_expected_file()
        {
            var actual = VoicemailConfRenderer.Render(SampleExtensions());
            Assert.Equal(Expected("voicemail.conf"), actual);
        }

        [Fact]
        public void Pjsip_without_nat_has_no_external_addresses()
        {
            var actual = PjsipConfRenderer.Render(new PjsipTransport(), SampleExtensions());
            Assert.DoesNotContain("external_", actual);
            Assert.DoesNotContain("local_net", actual);
        }

        [Theory]
        [InlineData("Evil\n[evil]\ntype = endpoint")]
        [InlineData("Evil; comment")]
        [InlineData("Evil\"quote")]
        public void Renderers_refuse_injection_even_if_validation_was_bypassed(string name)
        {
            // Simulates a row that got into the database without going through the repository.
            var extensions = new List<Extension>
            {
                new() { Number = "1001", Name = name, Secret = "AAAAbbbbCCCCdddd1111", VoicemailEnabled = true, VoicemailPin = "4321" },
            };

            Assert.Throws<InvalidOperationException>(() => PjsipConfRenderer.Render(new PjsipTransport(), extensions));
            Assert.Throws<InvalidOperationException>(() => ExtensionsConfRenderer.Render(extensions));
            Assert.Throws<InvalidOperationException>(() => VoicemailConfRenderer.Render(extensions));
        }

        [Fact]
        public void Pjsip_rejects_external_address_without_local_net()
        {
            var transport = new PjsipTransport { ExternalAddress = "203.0.113.10" };
            Assert.Throws<InvalidOperationException>(() => PjsipConfRenderer.Render(transport, SampleExtensions()));
        }

        /// <summary>
        /// A mailbox line is comma separated, and our own name rules allow commas, so a name like
        /// "Smith, John" must not shift the email into the name's place.
        /// </summary>
        [Fact]
        public void A_comma_in_a_name_cannot_split_the_mailbox_line()
        {
            var extensions = new List<Extension>
            {
                new()
                {
                    Number = "1001",
                    Name = "Smith, John",
                    Secret = "AAAAbbbbCCCCdddd1111",
                    VoicemailEnabled = true,
                    VoicemailPin = "4321",
                    VoicemailEmail = "john@example.com",
                },
            };

            var actual = VoicemailConfRenderer.Render(extensions);

            Assert.Contains("1001 => 4321,Smith  John,john@example.com,,attach=yes|delete=no\n", actual);
        }

        /// <summary>
        /// "delete=yes" means "delete once emailed". With nowhere to email it, that is just
        /// "delete", so it is never written for a mailbox without an address.
        /// </summary>
        [Fact]
        public void A_mailbox_with_no_email_never_deletes_or_attaches()
        {
            var extensions = new List<Extension>
            {
                new()
                {
                    Number = "1001",
                    Name = "Front Desk",
                    Secret = "AAAAbbbbCCCCdddd1111",
                    VoicemailEnabled = true,
                    VoicemailPin = "4321",
                    VoicemailAttachRecording = true,
                    VoicemailDeleteAfterEmail = true,
                },
            };

            Assert.Contains("1001 => 4321,Front Desk,,,attach=no|delete=no\n", VoicemailConfRenderer.Render(extensions));
        }

        [Fact]
        public void Delete_after_email_is_written_when_there_is_an_address_to_email()
        {
            var extensions = new List<Extension>
            {
                new()
                {
                    Number = "1001",
                    Name = "Front Desk",
                    Secret = "AAAAbbbbCCCCdddd1111",
                    VoicemailEnabled = true,
                    VoicemailPin = "4321",
                    VoicemailEmail = "desk@example.com",
                    VoicemailAttachRecording = false,
                    VoicemailDeleteAfterEmail = true,
                },
            };

            Assert.Contains("attach=no|delete=yes\n", VoicemailConfRenderer.Render(extensions));
        }

        [Fact]
        public void An_extension_without_voicemail_still_hangs_up_and_gets_no_mailbox()
        {
            var dialplan = ExtensionsConfRenderer.Render(WithoutVoicemail());
            var voicemail = VoicemailConfRenderer.Render(WithoutVoicemail());

            Assert.Contains("exten => 1001,1,Dial(PJSIP/1001,30,tTkK)\n same => n,Hangup()\n", dialplan);
            Assert.DoesNotContain("VoiceMail", dialplan);
            Assert.DoesNotContain("1001 =>", voicemail);
        }

        /// <summary>A feature code that can only ever say "no such mailbox" is not worth having.</summary>
        [Fact]
        public void The_voicemail_feature_code_appears_only_when_somebody_has_a_mailbox()
        {
            Assert.DoesNotContain(ExtensionsConfRenderer.VoicemailMainNumber, ExtensionsConfRenderer.Render(WithoutVoicemail()));
            Assert.Contains(ExtensionsConfRenderer.VoicemailMainNumber, ExtensionsConfRenderer.Render(SampleExtensions()));
        }

        [Fact]
        public void A_disabled_extension_gets_no_mailbox_even_with_voicemail_switched_on()
        {
            var voicemail = VoicemailConfRenderer.Render(SampleExtensions());

            Assert.DoesNotContain("1003", voicemail);
            Assert.DoesNotContain("9999", voicemail);
        }

        /// <summary>
        /// The mailbox context the dialplan sends calls to has to be the one voicemail.conf
        /// declares, or every message lands nowhere.
        /// </summary>
        [Fact]
        public void The_dialplan_and_the_mailboxes_agree_on_the_context()
        {
            var context = VoicemailConfRenderer.MailboxContext;

            Assert.Contains($"[{context}]", VoicemailConfRenderer.Render(SampleExtensions()));
            Assert.Contains($"VoiceMail(1002@{context},u)", ExtensionsConfRenderer.Render(SampleExtensions()));
            Assert.Contains($"VoiceMailMain(${{CALLERID(num)}}@{context},s)", ExtensionsConfRenderer.Render(SampleExtensions()));
        }

        /// <summary>
        /// A multi-device system carries max_contacts = N on every aor and loses remove_existing,
        /// which would otherwise delete every other contact the moment one device re-registered
        /// (D110). A single-device system is exactly the file it always was.
        /// </summary>
        [Fact]
        public void A_multi_device_system_keeps_its_contacts()
        {
            var extensions = new List<Extension>
            {
                new() { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
            };
            var transport = new PjsipTransport { MaxContacts = 2 };

            var actual = PjsipConfRenderer.Render(transport, extensions);

            Assert.Contains("max_contacts = 2\nremove_existing = no\n", actual);
            Assert.DoesNotContain("remove_existing = yes", actual);
        }

        [Fact]
        public void A_single_device_system_replaces_its_own_stale_contact()
        {
            var extensions = new List<Extension>
            {
                new() { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
            };

            var actual = PjsipConfRenderer.Render(new PjsipTransport(), extensions);

            Assert.Contains("max_contacts = 1\nremove_existing = yes\n", actual);
        }

        /// <summary>A setting value that never passed validation cannot reach a conf file (D110).</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(6)]
        public void An_invalid_max_contacts_setting_is_never_rendered(int maxContacts)
        {
            var extensions = new List<Extension>
            {
                new() { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
            };

            var transport = new PjsipTransport { MaxContacts = maxContacts };
            transport.Validate();

            Assert.Throws<InvalidOperationException>(() => PjsipConfRenderer.Render(transport, extensions));
        }
    }
}
