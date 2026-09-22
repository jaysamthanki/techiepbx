using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What a ring group turns into: an entry in the internal context that rings its members and
    /// then sends the call on, written as plain Dial() rather than a queue (D52).
    /// </summary>
    public class RingGroupRendererTests
    {
        private static List<Extension> SampleExtensions() => new()
        {
            new Extension { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
            new Extension
            {
                Number = "1002",
                Name = "Sales",
                Secret = "EEEEffffGGGGhhhh2222",
                VoicemailEnabled = true,
                VoicemailPin = "4321",
            },
            new Extension { Number = "1003", Name = "Disabled Phone", Secret = "IIIIjjjjKKKKllll3333", Enabled = false },
        };

        private static List<RingGroup> SampleGroups() => new()
        {
            // Out of order on purpose, plus one switched off and one nobody is left to ring.
            new RingGroup
            {
                RingGroupID = 2,
                Number = "601",
                Name = "Sales team",
                Strategy = "Hunt",
                Members = "1002,1001",
                RingSeconds = 15,
                DestinationType = "RingGroup",
                DestinationValue = "600",
            },
            new RingGroup
            {
                RingGroupID = 3,
                Number = "602",
                Name = "Switched off",
                Members = "1001",
                Enabled = false,
            },
            new RingGroup
            {
                RingGroupID = 4,
                Number = "603",
                Name = "Nobody left",
                Members = "1003",
            },
            new RingGroup
            {
                RingGroupID = 1,
                Number = "600",
                Name = "Support",
                Strategy = "All",
                Members = "1001,1002",
                RingSeconds = 20,
                CallerIDPrefix = "Support: ",
                DestinationType = "Voicemail",
                DestinationValue = "1002",
            },
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        private static string Render(params RingGroup[] groups) =>
            ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(), groups);

        [Fact]
        public void Ring_groups_match_expected_file()
        {
            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(), SampleGroups());

            Assert.Equal(Expected("extensions-ringgroups.conf"), actual);
        }

        /// <summary>
        /// A system with no ring groups has to render exactly the dialplan it rendered before ring
        /// groups existed.
        /// </summary>
        [Fact]
        public void A_system_with_no_ring_groups_renders_exactly_what_it_did_before()
        {
            var withEmpty = ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(), new List<RingGroup>());

            Assert.Equal(ExtensionsConfRenderer.Render(SampleExtensions()), withEmpty);
            Assert.DoesNotContain("Ring group", withEmpty);
        }

        /// <summary>Everyone at once, joined with &amp;, one Dial and one ring time.</summary>
        [Fact]
        public void Ring_all_rings_every_member_together()
        {
            var actual = Render(SampleGroups()[3]);

            Assert.Contains(" same => n,Dial(PJSIP/1001&PJSIP/1002,20,tTkKr)\n", actual);
        }

        /// <summary>
        /// One Dial per member, in the order the group lists them, and the ring time is per
        /// attempt rather than shared out between them (D52).
        /// </summary>
        [Fact]
        public void Hunt_rings_one_member_at_a_time_in_order()
        {
            var actual = Render(SampleGroups()[0]);

            Assert.Contains(" same => n,Dial(PJSIP/1002,15,tTkKr)\n same => n,Dial(PJSIP/1001,15,tTkKr)\n", actual);
            Assert.DoesNotContain("&", actual);
        }

        [Fact]
        public void The_caller_id_prefix_goes_in_front_of_the_callers_name()
        {
            Assert.Contains(" same => n,Set(CALLERID(name)=Support: ${CALLERID(name)})\n", Render(SampleGroups()[3]));
        }

        [Fact]
        public void A_group_with_no_prefix_does_not_touch_the_caller_id()
        {
            Assert.DoesNotContain("CALLERID(name)", Render(SampleGroups()[0]));
        }

        /// <summary>
        /// A comma would end the Set() argument early and quietly drop the rest of the prefix, so
        /// it is turned into a space the way a voicemail name is.
        /// </summary>
        [Fact]
        public void A_comma_in_the_prefix_cannot_cut_the_set_short()
        {
            var group = SampleGroups()[3];
            group.CallerIDPrefix = "Sales, Inc ";

            Assert.Contains(" same => n,Set(CALLERID(name)=Sales  Inc ${CALLERID(name)})\n", Render(group));
        }

        [Fact]
        public void A_call_nobody_answers_goes_to_the_groups_destination()
        {
            Assert.Contains(" same => n,VoiceMail(1002@default,u)\n same => n,Hangup()\n", Render(SampleGroups()[3]));
        }

        /// <summary>
        /// A group can hand on to another group, and it goes in by the front door like any other
        /// destination, so that group's own members and fallback apply (D54).
        /// </summary>
        [Fact]
        public void A_group_can_hand_on_to_another_group()
        {
            Assert.Contains(" same => n,Goto(internal,600,1)\n", Render(SampleGroups()[0]));
        }

        [Fact]
        public void A_switched_off_group_is_not_in_the_config_at_all()
        {
            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(), SampleGroups());

            Assert.DoesNotContain("602", actual);
            Assert.DoesNotContain("Switched off", actual);
        }

        /// <summary>
        /// A member that was deleted or switched off is dropped, and a group with nobody left is
        /// left out rather than written as a Dial with nothing to dial.
        /// </summary>
        [Fact]
        public void A_group_with_nobody_left_to_ring_is_left_out()
        {
            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(), SampleGroups());

            Assert.DoesNotContain("603", actual);
            Assert.DoesNotContain("Nobody left", actual);
        }

        [Fact]
        public void A_disabled_member_is_dropped_from_a_group_that_still_has_others()
        {
            var group = SampleGroups()[3];
            group.Members = "1001,1003,1002";

            Assert.Contains(" same => n,Dial(PJSIP/1001&PJSIP/1002,20,tTkKr)\n", Render(group));
        }

        [Fact]
        public void A_group_that_would_not_validate_is_never_written()
        {
            var group = new RingGroup { Number = "600", Name = "Broken", Members = "" };

            Assert.Throws<InvalidOperationException>(() => Render(group));
        }

        /// <summary>
        /// A row that reached the database another way still cannot open a section of its own or
        /// comment out what follows.
        /// </summary>
        [Theory]
        [InlineData("Evil\n[evil]\nexten => _X.,1,Dial(PJSIP/1001)")]
        [InlineData("Evil; comment")]
        [InlineData("Evil]")]
        public void Injection_through_a_group_name_is_refused(string name)
        {
            var group = new RingGroup { Number = "600", Name = name, Members = "1001" };

            Assert.Throws<InvalidOperationException>(() => Render(group));
        }
    }
}
