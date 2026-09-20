using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The cheat sheet's list of dialable codes (D120).
    ///
    /// Two kinds of test. The first kind checks that a code only appears when the thing behind it
    /// is switched on — a sheet pinned to a wall promising a park code on a system with parking
    /// off is worse than a sheet that never mentioned it. The second kind is the one that matters
    /// in a year's time: it renders the conf files and asserts that every code the sheet prints is
    /// really in them, so a renderer change cannot quietly leave the printout lying.
    /// </summary>
    public class FeatureCodesTests
    {
        private static List<Extension> SampleExtensions(bool voicemail) => new()
        {
            new Extension
            {
                Number = "1001",
                Name = "Front Desk",
                Secret = "AAAAbbbbCCCCdddd1111",
                VoicemailEnabled = voicemail,
                VoicemailPin = "4321",
            },
        };

        private static ParkingSettings ParkingOn() => new()
        {
            DtmfCode = "*3",
            Enabled = true,
            Slots = 3,
        };

        [Fact]
        public void Attended_and_blind_transfer_are_always_listed()
        {
            var codes = FeatureCodes.All(new ParkingSettings(), voicemailInUse: false);

            Assert.Contains(codes, c => c.Code == FeaturesConfRenderer.AttendedTransferCode);
            Assert.Contains(codes, c => c.Code == FeaturesConfRenderer.BlindTransferCode);
        }

        [Fact]
        public void Echo_test_is_always_listed()
        {
            var codes = FeatureCodes.All(new ParkingSettings(), voicemailInUse: false);

            Assert.Contains(codes, c => c.Code == ExtensionsConfRenderer.EchoTestNumber);
        }

        [Fact]
        public void Every_code_is_in_one_of_the_two_groups()
        {
            var codes = FeatureCodes.All(ParkingOn(), voicemailInUse: true);

            Assert.All(codes, c => Assert.Contains(c.Group, new[] { FeatureCodes.DuringACall, FeatureCodes.FromYourPhone }));
            Assert.All(codes, c => Assert.NotEqual("", c.Code));
            Assert.All(codes, c => Assert.NotEqual("", c.Description));
            Assert.All(codes, c => Assert.NotEqual("", c.Name));
        }

        /// <summary>
        /// The page groups by walking the list once, so every code of a group has to be together
        /// in it — otherwise a group would be printed twice.
        /// </summary>
        [Fact]
        public void Groups_are_contiguous_and_during_a_call_comes_first()
        {
            var codes = FeatureCodes.All(ParkingOn(), voicemailInUse: true);
            var groups = codes.Select(c => c.Group).ToList();

            // One change of group for two groups: any more means a group appeared twice.
            var changes = groups.Zip(groups.Skip(1)).Count(pair => pair.First != pair.Second);

            Assert.Equal(FeatureCodes.DuringACall, groups.First());
            Assert.Equal(2, groups.Distinct().Count());
            Assert.Equal(1, changes);
        }

        [Fact]
        public void Park_code_follows_the_setting()
        {
            var parking = ParkingOn();
            parking.DtmfCode = "*70";

            var codes = FeatureCodes.All(parking, voicemailInUse: false);

            Assert.Contains(codes, c => c.Code == "*70" && c.Group == FeatureCodes.DuringACall);
        }

        [Fact]
        public void Parking_off_lists_neither_the_park_code_nor_the_slots()
        {
            var codes = FeatureCodes.All(new ParkingSettings(), voicemailInUse: false);

            Assert.DoesNotContain(codes, c => c.Code == ParkingSettings.DefaultDtmfCode);
            Assert.DoesNotContain(codes, c => c.Group == FeatureCodes.FromYourPhone && c.Code.StartsWith('1'));
        }

        [Fact]
        public void Parking_on_lists_the_park_code_and_the_slot_range()
        {
            var codes = FeatureCodes.All(ParkingOn(), voicemailInUse: false);

            Assert.Contains(codes, c => c.Code == "*3" && c.Group == FeatureCodes.DuringACall);
            Assert.Contains(codes, c => c.Code == "1-3" && c.Group == FeatureCodes.FromYourPhone);
        }

        /// <summary>One slot is not a range, and "1-1" would be a nonsense to read on a wall.</summary>
        [Fact]
        public void Single_slot_is_written_as_one_number()
        {
            var parking = ParkingOn();
            parking.Slots = 1;

            var codes = FeatureCodes.All(parking, voicemailInUse: false);

            Assert.Contains(codes, c => c.Code == "1" && c.Group == FeatureCodes.FromYourPhone);
        }

        /// <summary>
        /// The grounding test. Every star code the sheet prints has to be in the generated files,
        /// and the slot numbers have to be extensions of the dialplan — the sheet is a reading of
        /// the config, not a second source of truth for it.
        /// </summary>
        [Fact]
        public void The_printed_codes_are_in_the_generated_config()
        {
            var parking = ParkingOn();
            var extensions = SampleExtensions(voicemail: true);

            var dialplan = ExtensionsConfRenderer.Render(
                extensions,
                new List<Trunk>(),
                new List<OutboundRoute>(),
                new List<InboundRoute>(),
                new List<RingGroup>(),
                new List<Announcement>(),
                new List<Ivr>(),
                new List<TimeCondition>(),
                AsteriskSettings.DefaultTimezone,
                parking);

            var features = FeaturesConfRenderer.Render(parking);
            var codes = FeatureCodes.All(parking, voicemailInUse: true);

            foreach (var code in codes.Where(c => c.Code.StartsWith('*')))
                Assert.True(dialplan.Contains($"exten => {code.Code},") || dialplan.Contains($"exten => _{code.Code}.") || features.Contains($"> {code.Code}\n"), $"{code.Code} is printed but not generated");

            // The slots, one extension each, exactly as many as the settings say.
            foreach (var slot in parking.SlotNumbers)
                Assert.Contains($"exten => {slot},1,ParkedCall(", dialplan);

            Assert.DoesNotContain($"exten => {parking.Slots + 1},1,ParkedCall(", dialplan);
        }

        [Fact]
        public void Voicemail_code_only_appears_when_somebody_has_a_mailbox()
        {
            var without = FeatureCodes.All(new ParkingSettings(), voicemailInUse: false);
            var with = FeatureCodes.All(new ParkingSettings(), voicemailInUse: true);

            Assert.DoesNotContain(without, c => c.Code == ExtensionsConfRenderer.VoicemailMainNumber);
            Assert.Contains(with, c => c.Code == ExtensionsConfRenderer.VoicemailMainNumber);
        }

        /// <summary>
        /// And the test the page relies on: *97 is generated exactly when an enabled extension has
        /// a mailbox, which is the flag the page hands <see cref="FeatureCodes.All"/>.
        /// </summary>
        [Fact]
        public void Voicemail_code_generation_matches_the_flag_the_page_passes()
        {
            var withMailbox = ExtensionsConfRenderer.Render(SampleExtensions(voicemail: true));
            var without = ExtensionsConfRenderer.Render(SampleExtensions(voicemail: false));

            Assert.Contains($"exten => {ExtensionsConfRenderer.VoicemailMainNumber},", withMailbox);
            Assert.DoesNotContain($"exten => {ExtensionsConfRenderer.VoicemailMainNumber},", without);
        }
    }
}
