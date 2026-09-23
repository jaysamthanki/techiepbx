using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The picker list is worked out from what exists rather than stored, so these tests are
    /// mostly about what it refuses to offer (D35).
    /// </summary>
    public class DestinationCatalogTests
    {
        private static List<Extension> SampleExtensions() => new()
        {
            new Extension { Number = "1002", Name = "Sales", Secret = "EEEEffffGGGGhhhh2222", VoicemailEnabled = true, VoicemailPin = "4321" },
            new Extension { Number = "1003", Name = "Disabled Phone", Secret = "IIIIjjjjKKKKllll3333", Enabled = false, VoicemailEnabled = true, VoicemailPin = "9999" },
            new Extension { Number = "999", Name = "Warehouse", Secret = "MMMMnnnnOOOOpppp4444" },
            new Extension { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
        };

        [Fact]
        public void Every_enabled_extension_can_be_dialled_to()
        {
            var extensions = DestinationCatalog.All(SampleExtensions())
                .Where(c => c.Destination.Type == DestinationType.Extension)
                .ToList();

            Assert.Equal(new[] { "999", "1001", "1002" }, extensions.Select(c => c.Destination.Value));
            Assert.All(extensions, c => Assert.Equal(DestinationCatalog.ExtensionsGroup, c.GroupName));
            Assert.Equal("1001 Front Desk", extensions[1].Label);
        }

        /// <summary>A disabled extension is not in the generated config, so calls sent to it vanish.</summary>
        [Fact]
        public void A_disabled_extension_is_not_offered_at_all()
        {
            var keys = DestinationCatalog.All(SampleExtensions()).Select(c => c.Destination.Key).ToList();

            Assert.DoesNotContain("Extension:1003", keys);
            Assert.DoesNotContain("Voicemail:1003", keys);
        }

        [Fact]
        public void Only_the_mailboxes_that_exist_are_offered()
        {
            var mailboxes = DestinationCatalog.All(SampleExtensions())
                .Where(c => c.Destination.Type == DestinationType.Voicemail)
                .ToList();

            Assert.Equal(new[] { "1002" }, mailboxes.Select(c => c.Destination.Value));
            Assert.Equal(DestinationCatalog.VoicemailGroup, mailboxes[0].GroupName);
        }

        [Fact]
        public void Hanging_up_is_always_on_the_list_and_comes_last()
        {
            var choices = DestinationCatalog.All(new List<Extension>());

            Assert.Single(choices);
            Assert.Equal(DestinationType.Hangup, choices[0].Destination.Type);
            Assert.Equal(DestinationCatalog.OtherGroup, choices[0].GroupName);
            Assert.Equal(DestinationType.Hangup, DestinationCatalog.All(SampleExtensions())[^1].Destination.Type);
        }

        [Fact]
        public void The_list_is_extensions_then_mailboxes_then_the_rest()
        {
            var groups = DestinationCatalog.All(SampleExtensions()).Select(c => c.GroupName).Distinct();

            Assert.Equal(
                new[] { DestinationCatalog.ExtensionsGroup, DestinationCatalog.VoicemailGroup, DestinationCatalog.OtherGroup },
                groups);
        }

        [Fact]
        public void Every_choice_offered_is_one_the_dialplan_could_be_written_for()
        {
            Assert.All(DestinationCatalog.All(SampleExtensions()), c => Assert.Empty(c.Destination.Validate()));
        }

        [Fact]
        public void Find_labels_a_destination_that_still_points_at_something()
        {
            var found = DestinationCatalog.Find(SampleExtensions(), new Destination(DestinationType.Extension, "1001"));

            Assert.NotNull(found);
            Assert.Equal("1001 Front Desk", found.Label);
        }

        /// <summary>
        /// This is the dangling reference a feature has to notice: the row it stored still reads,
        /// but what it pointed at is gone, disabled, or has had its voicemail switched off.
        /// </summary>
        [Theory]
        [InlineData(DestinationType.Extension, "1003")]
        [InlineData(DestinationType.Extension, "4000")]
        [InlineData(DestinationType.Voicemail, "1001")]
        public void Find_answers_null_for_a_destination_that_no_longer_points_at_anything(DestinationType type, string value)
        {
            Assert.Null(DestinationCatalog.Find(SampleExtensions(), new Destination(type, value)));
        }

        [Fact]
        public void Find_answers_null_for_nothing()
        {
            Assert.Null(DestinationCatalog.Find(SampleExtensions(), null));
        }

        /// <summary>
        /// Ring groups are the first feature to add a source of its own, which is what D35 said
        /// every later feature would do (D54).
        /// </summary>
        [Fact]
        public void Ring_groups_are_on_the_list_too()
        {
            var groups = SampleGroups();

            var choices = DestinationCatalog.All(SampleExtensions(), groups)
                .Where(c => c.Destination.Type == DestinationType.RingGroup)
                .ToList();

            Assert.Equal(new[] { "600" }, choices.Select(c => c.Destination.Value));
            Assert.Equal(DestinationCatalog.RingGroupsGroup, choices[0].GroupName);
            Assert.Equal("600 Support", choices[0].Label);
        }

        [Fact]
        public void A_disabled_ring_group_is_not_offered()
        {
            var keys = DestinationCatalog.All(SampleExtensions(), SampleGroups()).Select(c => c.Destination.Key);

            Assert.DoesNotContain("RingGroup:601", keys);
        }

        [Fact]
        public void Ring_groups_come_after_the_mailboxes_and_before_hanging_up()
        {
            var groups = DestinationCatalog.All(SampleExtensions(), SampleGroups()).Select(c => c.GroupName).Distinct();

            Assert.Equal(
                new[]
                {
                    DestinationCatalog.ExtensionsGroup,
                    DestinationCatalog.VoicemailGroup,
                    DestinationCatalog.RingGroupsGroup,
                    DestinationCatalog.OtherGroup,
                },
                groups);
        }

        [Fact]
        public void Find_labels_a_ring_group_and_answers_null_for_one_that_is_gone()
        {
            var groups = SampleGroups();

            Assert.Equal(
                "600 Support",
                DestinationCatalog.Find(SampleExtensions(), groups, new Destination(DestinationType.RingGroup, "600"))!.Label);

            Assert.Null(DestinationCatalog.Find(SampleExtensions(), groups, new Destination(DestinationType.RingGroup, "601")));
            Assert.Null(DestinationCatalog.Find(SampleExtensions(), groups, new Destination(DestinationType.RingGroup, "999")));
        }

        /// <summary>A caller that knows nothing of ring groups still gets the list it always did.</summary>
        [Fact]
        public void The_older_overload_offers_no_ring_groups()
        {
            Assert.DoesNotContain(
                DestinationType.RingGroup,
                DestinationCatalog.All(SampleExtensions()).Select(c => c.Destination.Type));
        }

        /// <summary>
        /// A picker leaves off only the entity being edited: other entities of the same kind stay,
        /// because handing on to another group is a feature (D54).
        /// </summary>
        [Fact]
        public void Except_leaves_off_only_the_one_being_edited()
        {
            var groups = SampleGroups().Append(new RingGroup { Number = "602", Name = "Sales", Members = "1002" }).ToList();
            var all = DestinationCatalog.All(SampleExtensions(), groups);

            var keys = DestinationCatalog.Except(all, new Destination(DestinationType.RingGroup, "600"))
                .Select(c => c.Destination.Key)
                .ToList();

            Assert.DoesNotContain("RingGroup:600", keys);
            Assert.Contains("RingGroup:602", keys);
            Assert.Equal(all.Count - 1, keys.Count);
        }

        /// <summary>Something not saved yet cannot be pointed at, so there is nothing to leave off.</summary>
        [Fact]
        public void Except_nothing_leaves_the_list_whole()
        {
            var all = DestinationCatalog.All(SampleExtensions(), SampleGroups());

            Assert.Equal(
                all.Select(c => c.Destination.Key),
                DestinationCatalog.Except(all, null).Select(c => c.Destination.Key));
        }

        /// <summary>
        /// Call flow controls are a source of their own (F9), listed by code — shortest first, the
        /// way the renderer writes them — and keyed by the code, which is what the dialplan's entry
        /// context answers on.
        /// </summary>
        [Fact]
        public void Call_flow_controls_are_on_the_list_by_code()
        {
            var choices = AllWithControls()
                .Where(c => c.Destination.Type == DestinationType.CallFlowControl)
                .ToList();

            Assert.Equal(new[] { "*28", "*29", "*271" }, choices.Select(c => c.Destination.Value));
            Assert.Equal(new[] { "CallFlowControl:*28", "CallFlowControl:*29", "CallFlowControl:*271" }, choices.Select(c => c.Destination.Key));
            Assert.All(choices, c => Assert.Equal(DestinationCatalog.CallFlowControlsGroup, c.GroupName));
            Assert.Equal("*28 Night mode", choices[0].Label);
        }

        [Fact]
        public void Call_flow_controls_come_last_before_hanging_up()
        {
            var groups = AllWithControls().Select(c => c.GroupName).Distinct().ToList();

            Assert.Equal(DestinationCatalog.CallFlowControlsGroup, groups[^2]);
            Assert.Equal(DestinationCatalog.OtherGroup, groups[^1]);
        }

        [Fact]
        public void Every_call_flow_control_offered_is_one_the_dialplan_could_be_written_for()
        {
            Assert.All(AllWithControls(), c => Assert.Empty(c.Destination.Validate()));
        }

        [Fact]
        public void Find_labels_a_call_flow_control_and_answers_null_for_one_that_is_gone()
        {
            Assert.Equal(
                "*29 Lunch",
                DestinationCatalog.Find(
                    SampleExtensions(), new List<RingGroup>(), new List<Announcement>(), new List<Ivr>(), new List<TimeCondition>(),
                    SampleControls(), new Destination(DestinationType.CallFlowControl, "*29"))!.Label);

            Assert.Null(DestinationCatalog.Find(
                SampleExtensions(), new List<RingGroup>(), new List<Announcement>(), new List<Ivr>(), new List<TimeCondition>(),
                SampleControls(), new Destination(DestinationType.CallFlowControl, "*30")));
        }

        /// <summary>A caller that knows nothing of call flow controls still gets the list it always did.</summary>
        [Fact]
        public void The_older_overloads_offer_no_call_flow_controls()
        {
            Assert.DoesNotContain(
                DestinationType.CallFlowControl,
                DestinationCatalog.All(
                    SampleExtensions(), new List<RingGroup>(), new List<Announcement>(), new List<Ivr>(), new List<TimeCondition>())
                    .Select(c => c.Destination.Type));
        }

        /// <summary>
        /// A switch's picker leaves off only the switch being edited: the others stay, because one
        /// switch handing to another is how they chain (D136).
        /// </summary>
        [Fact]
        public void Except_leaves_off_the_call_flow_control_being_edited()
        {
            var all = AllWithControls();

            var keys = DestinationCatalog.Except(all, SampleControls()[0].ToDestination())
                .Select(c => c.Destination.Key)
                .ToList();

            Assert.DoesNotContain("CallFlowControl:*28", keys);
            Assert.Contains("CallFlowControl:*29", keys);
            Assert.Contains("CallFlowControl:*271", keys);
            Assert.Equal(all.Count - 1, keys.Count);
        }

        private static List<DestinationChoice> AllWithControls() =>
            DestinationCatalog.All(
                SampleExtensions(), SampleGroups(), new List<Announcement>(), new List<Ivr>(), new List<TimeCondition>(),
                SampleControls());

        /// <summary>Out of code order on purpose, to prove the catalog sorts them.</summary>
        private static List<CallFlowControl> SampleControls() => new()
        {
            new CallFlowControl { CallFlowControlID = 1, Name = "Night mode", FeatureCode = "*28" },
            new CallFlowControl { CallFlowControlID = 2, Name = "Holiday", FeatureCode = "*271" },
            new CallFlowControl { CallFlowControlID = 3, Name = "Lunch", FeatureCode = "*29" },
        };

        private static List<RingGroup> SampleGroups() => new()
        {
            new RingGroup { RingGroupID = 1, Number = "600", Name = "Support", Members = "1001" },
            new RingGroup { RingGroupID = 2, Number = "601", Name = "Switched off", Members = "1001", Enabled = false },
        };
    }
}
