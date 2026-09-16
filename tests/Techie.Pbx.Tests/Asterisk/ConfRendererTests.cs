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

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        [Fact]
        public void Pjsip_matches_expected_file()
        {
            var actual = PjsipConfRenderer.Render(NatTransport(), SampleExtensions());
            Assert.Equal(Expected("pjsip.conf"), actual);
        }

        [Fact]
        public void Extensions_matches_expected_file()
        {
            var actual = ExtensionsConfRenderer.Render(SampleExtensions());
            Assert.Equal(Expected("extensions.conf"), actual);
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

            Assert.Contains("exten => 1001,1,Dial(PJSIP/1001,30)\n same => n,Hangup()\n", dialplan);
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
    }
}
