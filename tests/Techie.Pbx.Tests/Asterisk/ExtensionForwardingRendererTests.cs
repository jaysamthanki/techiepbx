using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What a forwarding list turns into: the same one Dial the extension always had, ringing the
    /// list instead of the extension's own phone (D130). Everything around it — the hint, the ring
    /// time, the voicemail fallthrough — has to come out exactly as it did before forwarding
    /// existed, which is most of what these tests are checking.
    /// </summary>
    public class ExtensionForwardingRendererTests
    {
        private static List<Extension> SampleExtensions() => new()
        {
            // An extension and a mobile at once, with no mailbox behind them.
            new Extension
            {
                Number = "1001",
                Name = "Front Desk",
                Secret = "AAAAbbbbCCCCdddd1111",
                Forwarding = "1002 7146085242",
            },

            // Nothing forwarded: this entry is the one that must not have changed at all.
            new Extension
            {
                Number = "1002",
                Name = "O'Brien (Sales)",
                Secret = "EEEEffffGGGGhhhh2222",
                VoicemailEnabled = true,
                VoicemailPin = "4321",
                VoicemailEmail = "sales@example.com",
            },

            // The mobile on its own, and the mailbox still catching what it does not answer.
            new Extension
            {
                Number = "1003",
                Name = "Cell Only",
                Secret = "IIIIjjjjKKKKllll3333",
                Forwarding = "7146085242",
                VoicemailEnabled = true,
                VoicemailPin = "9999",
            },

            // Its own number in its own list, which is how somebody keeps their desk phone
            // ringing while adding a mobile to it.
            new Extension
            {
                Number = "1004",
                Name = "Two Phones",
                Secret = "MMMMnnnnOOOOpppp4444",
                Forwarding = "1004 7146085242",
            },
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        [Fact]
        public void Forwarding_matches_expected_file()
        {
            var actual = ExtensionsConfRenderer.Render(SampleExtensions());

            Assert.Equal(Expected("extensions-forwarding.conf"), actual);
        }

        /// <summary>
        /// An extension with nothing forwarded renders exactly the dialplan it rendered before
        /// forwarding existed — the same golden file the plain extensions test uses.
        /// </summary>
        [Fact]
        public void An_empty_field_renders_exactly_what_it_did_before()
        {
            var extensions = SampleExtensions();
            foreach (var extension in extensions)
                extension.Forwarding = "";

            var actual = ExtensionsConfRenderer.Render(extensions);

            Assert.Contains("exten => 1001,1,Dial(PJSIP/1001,30,tTkKr)\n same => n,Hangup()\n", actual);
            Assert.DoesNotContain("Local/", actual);
            Assert.DoesNotContain("Forwarding rings", actual);
        }

        /// <summary>
        /// The targets are written in the order they were typed, and in one Dial: ring all is the
        /// whole point, so two Dials would be two rings one after the other.
        /// </summary>
        [Fact]
        public void The_targets_are_dialled_together_in_the_order_they_were_given()
        {
            var extensions = SampleExtensions();
            extensions[0].Forwarding = "7146085242 1002";

            var actual = ExtensionsConfRenderer.Render(extensions);

            Assert.Contains("exten => 1001,1,Dial(Local/7146085242@internal/n&PJSIP/1002,30,tTkKr)\n", actual);
        }

        /// <summary>
        /// The ring time and the no-answer fallthrough belong to the extension, not to the
        /// forwarding list: a forwarded extension with a mailbox still lands in it.
        /// </summary>
        [Fact]
        public void The_ring_time_and_the_voicemail_fallthrough_are_unchanged()
        {
            var actual = ExtensionsConfRenderer.Render(SampleExtensions());

            Assert.Contains(
                "exten => 1003,1,Dial(Local/7146085242@internal/n,30,tTkKr)\n" +
                " same => n,GotoIf($[\"${DIALSTATUS}\" = \"BUSY\"]?busy:unavailable)\n" +
                " same => n(busy),VoiceMail(1003@default,b)\n",
                actual);
        }

        /// <summary>
        /// A forwarded extension is still watchable from a phone key: the lamp follows the handset,
        /// which is what a BLF key is for (D121).
        /// </summary>
        [Fact]
        public void A_forwarded_extension_still_has_its_own_hint()
        {
            var actual = ExtensionsConfRenderer.Render(SampleExtensions());

            Assert.Contains("exten => 1001,hint,PJSIP/1001\n", actual);
        }

        /// <summary>
        /// The hold music backfill is written on the extension's own line and named on every leg of
        /// the Dial, forwarded or not (D122 amended).
        /// </summary>
        [Fact]
        public void Forwarding_keeps_the_hold_music_options_on_the_dial()
        {
            var classes = new List<MohClass> { new() { MohClassID = 1, Name = "Standard", Directory = "default", IsDefault = true } };

            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                new List<RingGroup>(), new List<Announcement>(), new List<Ivr>(), new List<TimeCondition>(),
                AsteriskSettings.DefaultTimezone, new ParkingSettings(), classes);

            Assert.Contains(" same => n,Dial(PJSIP/1002&Local/7146085242@internal/n,30,tTkKrU(sub-setmoh))\n", actual);
        }

        /// <summary>
        /// A target that is an extension which has since been switched off stays an endpoint: a
        /// phone that does not ring. The one thing it must never become is a number offered to the
        /// outbound routes.
        /// </summary>
        [Fact]
        public void A_target_that_is_a_disabled_extension_is_still_dialled_as_an_extension()
        {
            var extensions = SampleExtensions();
            extensions[1].Enabled = false;

            var actual = ExtensionsConfRenderer.Render(extensions);

            Assert.Contains("exten => 1001,1,Dial(PJSIP/1002&Local/7146085242@internal/n,30,tTkKr)\n", actual);
            Assert.DoesNotContain("Local/1002@", actual);
        }

        /// <summary>
        /// The renderer re-validates every extension it is given (D12), so a row that reached the
        /// database another way cannot write a forwarding target into a Dial.
        /// </summary>
        [Fact]
        public void A_forwarding_list_that_does_not_validate_is_refused()
        {
            var extensions = SampleExtensions();
            extensions[0].Forwarding = "011441234567890";

            Assert.Throws<InvalidOperationException>(() => ExtensionsConfRenderer.Render(extensions));
        }
    }
}
