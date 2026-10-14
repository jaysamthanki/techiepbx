using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Ring-all-contacts dialling (D179). <c>Dial(PJSIP/101)</c> rings exactly one contact —
    /// chan_pjsip picks the first reachable one — so every internal Dial goes through
    /// <c>${PJSIP_DIAL_CONTACTS(...)}</c>, which expands to every reachable contact joined by
    /// <c>&amp;</c> and to nothing when there is none. Dial skips empty <c>&amp;</c> members,
    /// so a dead endpoint drops out of a fork harmlessly — but a Dial whose <b>whole</b>
    /// argument is empty errors and hangs the caller up, so wherever there is somewhere better
    /// to land, a GotoIf guard tests the same reads first. A separate GotoIf line, never an
    /// ExecIf around the Dial: ExecIf splits its arguments on commas and the Dial's own commas
    /// would truncate it.
    /// </summary>
    public class DialContactsRendererTests
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
            new Extension
            {
                Number = "1003",
                Name = "Cell Only",
                Secret = "IIIIjjjjKKKKllll3333",
                VoicemailEnabled = true,
                VoicemailPin = "4321",
            },
        };

        private static string RenderGroup(RingGroup group) =>
            ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                new List<RingGroup> { group });

        /// <summary>
        /// The guard reads the same function the Dial does, so an extension whose every contact
        /// is gone lands in the unavailable greeting exactly as a dead endpoint does, instead of
        /// the empty Dial erroring and hanging the caller up.
        /// </summary>
        [Fact]
        public void An_extension_with_voicemail_guards_its_dial_against_an_empty_contact_list()
        {
            var actual = ExtensionsConfRenderer.Render(SampleExtensions());

            Assert.Contains(
                "exten => 1002,1,GotoIf($[\"${PJSIP_DIAL_CONTACTS(1002)}\" = \"\"]?unavailable)\n" +
                " same => n,Dial(${PJSIP_DIAL_CONTACTS(1002)},30,tTkKr)\n" +
                " same => n,GotoIf($[\"${DIALSTATUS}\" = \"BUSY\"]?busy:unavailable)\n",
                actual);
        }

        /// <summary>
        /// Without a mailbox there is no unavailable label to jump to: the erroring Dial ends
        /// the call, which is what a dead endpoint did before the guard existed.
        /// </summary>
        [Fact]
        public void An_extension_without_voicemail_gets_no_guard()
        {
            var actual = ExtensionsConfRenderer.Render(SampleExtensions());

            Assert.Contains("exten => 1001,1,Dial(${PJSIP_DIAL_CONTACTS(1001)},30,tTkKr)\n same => n,Hangup()\n", actual);
        }

        /// <summary>
        /// A web-client extension's guard concatenates the same two reads its Dial forks — with
        /// nothing between them, so the test is empty exactly when both are.
        /// </summary>
        [Fact]
        public void A_web_client_extensions_guard_tests_both_endpoints_reads()
        {
            var extensions = SampleExtensions();
            extensions[1].WebClient = true;

            var actual = ExtensionsConfRenderer.Render(extensions);

            Assert.Contains(
                "exten => 1002,1,GotoIf($[\"${PJSIP_DIAL_CONTACTS(1002)}${PJSIP_DIAL_CONTACTS(1002-web)}\" = \"\"]?unavailable)\n" +
                " same => n,Dial(${PJSIP_DIAL_CONTACTS(1002)}&${PJSIP_DIAL_CONTACTS(1002-web)},30,tTkKr)\n",
                actual);
        }

        /// <summary>
        /// A forwarded extension whose every target is an extension guards the whole target
        /// list: the Dial is all-empty exactly when every target's contacts are.
        /// </summary>
        [Fact]
        public void A_forwarded_extensions_guard_covers_the_whole_target_list()
        {
            var extensions = SampleExtensions();
            extensions[1].Forwarding = "1001 1003";

            var actual = ExtensionsConfRenderer.Render(extensions);

            Assert.Contains(
                "exten => 1002,1,GotoIf($[\"${PJSIP_DIAL_CONTACTS(1001)}${PJSIP_DIAL_CONTACTS(1003)}\" = \"\"]?unavailable)\n" +
                " same => n,Dial(${PJSIP_DIAL_CONTACTS(1001)}&${PJSIP_DIAL_CONTACTS(1003)},30,tTkKr)\n",
                actual);
        }

        /// <summary>
        /// A forwarding list with any Local/ target gets no guard at all: a Local/ channel is
        /// always dialable, so that Dial can never be all-empty — and a guard testing only the
        /// extension targets would send the call to voicemail without ever ringing the number
        /// the Local/ leg is for.
        /// </summary>
        [Fact]
        public void A_forwarding_list_with_an_outside_number_gets_no_guard()
        {
            var extensions = SampleExtensions();
            extensions[1].Forwarding = "1001 7146085242";
            extensions[2].Forwarding = "7146085242";

            var actual = ExtensionsConfRenderer.Render(extensions);

            Assert.Contains("exten => 1002,1,Dial(${PJSIP_DIAL_CONTACTS(1001)}&Local/7146085242@internal/n,30,tTkKr)\n", actual);
            Assert.Contains(
                "exten => 1003,1,Dial(Local/7146085242@internal/n,30,tTkKr)\n" +
                " same => n,GotoIf($[\"${DIALSTATUS}\" = \"BUSY\"]?busy:unavailable)\n",
                actual);
        }

        /// <summary>
        /// Ring all: one guard over every member's reads, jumping past the Dial to the
        /// nomembers line so the group's own destination still runs when nobody is reachable.
        /// A member whose own reads are empty just drops out of the fork — Dial skips empty
        /// &amp; members — which is why the guard only has to catch all-empty.
        /// </summary>
        [Fact]
        public void A_ring_all_group_jumps_past_the_dial_when_no_member_is_reachable()
        {
            var group = new RingGroup
            {
                RingGroupID = 1,
                Number = "600",
                Name = "Support",
                Strategy = "All",
                Members = "1001,1002",
                RingSeconds = 20,
                DestinationType = "Voicemail",
                DestinationValue = "1002",
            };

            Assert.Contains(
                " same => n,GotoIf($[\"${PJSIP_DIAL_CONTACTS(1001)}${PJSIP_DIAL_CONTACTS(1002)}\" = \"\"]?nomembers)\n" +
                " same => n,Dial(${PJSIP_DIAL_CONTACTS(1001)}&${PJSIP_DIAL_CONTACTS(1002)},20,tTkKr)\n" +
                " same => n(nomembers),NoOp(No reachable group members)\n" +
                " same => n,VoiceMail(1002@default,u)\n",
                RenderGroup(group));
        }

        /// <summary>
        /// Hunt: each member's Dial has a guard of its own that jumps past it to the next
        /// member's guard — past the last member to the afterhunt line — so an unreachable
        /// member is skipped and a group of nothing but dead endpoints still reaches the
        /// destination. A dial that completed unanswered falls through to the next guard the
        /// way it always fell through to the next Dial.
        /// </summary>
        [Fact]
        public void A_hunt_group_skips_each_unreachable_member_to_the_next()
        {
            var group = new RingGroup
            {
                RingGroupID = 2,
                Number = "601",
                Name = "Sales team",
                Strategy = "Hunt",
                Members = "1001,1002,1003",
                RingSeconds = 15,
            };

            Assert.Contains(
                "exten => 601,1,NoOp(Ring group 601 Sales team)\n" +
                " same => n,GotoIf($[\"${PJSIP_DIAL_CONTACTS(1001)}\" = \"\"]?m2)\n" +
                " same => n,Dial(${PJSIP_DIAL_CONTACTS(1001)},15,tTkKr)\n" +
                " same => n(m2),GotoIf($[\"${PJSIP_DIAL_CONTACTS(1002)}\" = \"\"]?m3)\n" +
                " same => n,Dial(${PJSIP_DIAL_CONTACTS(1002)},15,tTkKr)\n" +
                " same => n(m3),GotoIf($[\"${PJSIP_DIAL_CONTACTS(1003)}\" = \"\"]?afterhunt)\n" +
                " same => n,Dial(${PJSIP_DIAL_CONTACTS(1003)},15,tTkKr)\n" +
                " same => n(afterhunt),NoOp(No hunt members left to try)\n",
                RenderGroup(group));
        }

        /// <summary>
        /// A hunt member with the web client forks both devices in its own Dial and tests both
        /// reads in its own guard, like a direct dial does.
        /// </summary>
        [Fact]
        public void A_hunt_members_web_client_is_in_its_guard_and_its_dial()
        {
            var extensions = SampleExtensions();
            extensions[0].WebClient = true;

            var group = new RingGroup
            {
                RingGroupID = 2,
                Number = "601",
                Name = "Sales team",
                Strategy = "Hunt",
                Members = "1001,1002",
                RingSeconds = 15,
            };

            var actual = ExtensionsConfRenderer.Render(
                extensions, new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                new List<RingGroup> { group });

            Assert.Contains(
                " same => n,GotoIf($[\"${PJSIP_DIAL_CONTACTS(1001)}${PJSIP_DIAL_CONTACTS(1001-web)}\" = \"\"]?m2)\n" +
                " same => n,Dial(${PJSIP_DIAL_CONTACTS(1001)}&${PJSIP_DIAL_CONTACTS(1001-web)},15,tTkKr)\n",
                actual);
        }
    }
}
