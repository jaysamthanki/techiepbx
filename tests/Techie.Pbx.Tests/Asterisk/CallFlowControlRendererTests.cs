using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What a call flow control turns into (F9): a toggle and a hint on its code in the internal
    /// context, a door in by code in the entry context, and a context of its own that reads the
    /// switch's state from astdb at call time — so the file never changes when a phone flips it.
    /// </summary>
    public class CallFlowControlRendererTests
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
        };

        /// <summary>
        /// Out of code order on purpose, and the second hands its override to the first, which is
        /// how two switches chain.
        /// </summary>
        private static List<CallFlowControl> SampleControls() => new()
        {
            new CallFlowControl
            {
                CallFlowControlID = 2,
                Name = "Lunch",
                FeatureCode = "*271",
                NormalDestinationType = "Extension",
                NormalDestinationValue = "1002",
                OverrideDestinationType = "CallFlowControl",
                OverrideDestinationValue = "*28",
            },
            new CallFlowControl
            {
                CallFlowControlID = 1,
                Name = "Night mode",
                FeatureCode = "*28",
                NormalDestinationType = "Extension",
                NormalDestinationValue = "1001",
                OverrideDestinationType = "Voicemail",
                OverrideDestinationValue = "1002",
            },
        };

        private static ParkingSettings Parking() => new() { Enabled = true, Slots = 2 };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        private static string Render(IEnumerable<CallFlowControl> controls, ParkingSettings? parking = null) =>
            ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                new List<RingGroup>(), new List<Announcement>(), new List<Ivr>(), new List<TimeCondition>(),
                AsteriskSettings.DefaultTimezone, parking ?? new ParkingSettings(), new List<MohClass>(), controls);

        /// <summary>The text between a context's header and the next one.</summary>
        private static string Context(string actual, string name)
        {
            var start = actual.IndexOf($"\n[{name}]\n", StringComparison.Ordinal);
            Assert.NotEqual(-1, start);

            var end = actual.IndexOf("\n[", start + 1, StringComparison.Ordinal);
            return end == -1 ? actual[start..] : actual[start..end];
        }

        [Fact]
        public void Call_flow_controls_match_expected_file()
        {
            Assert.Equal(Expected("extensions-call-flow-controls.conf"), Render(SampleControls(), Parking()));
        }

        /// <summary>
        /// A system with no switches has to render exactly the dialplan it rendered before they
        /// existed: no toggle, no entry context, no switch context.
        /// </summary>
        [Fact]
        public void A_system_with_no_call_flow_controls_renders_exactly_what_it_did_before()
        {
            var withEmpty = Render(new List<CallFlowControl>(), Parking());

            Assert.Equal(
                ExtensionsConfRenderer.Render(
                    SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>(),
                    new List<RingGroup>(), new List<Announcement>(), new List<Ivr>(), new List<TimeCondition>(),
                    AsteriskSettings.DefaultTimezone, Parking(), new List<MohClass>()),
                withEmpty);

            Assert.DoesNotContain("[cfc-", withEmpty);
            Assert.DoesNotContain(ExtensionsConfRenderer.CallFlowControlEntryContext, withEmpty);
            Assert.DoesNotContain("TNPBX/CFC", withEmpty);
        }

        /// <summary>
        /// The switch's context reads <c>TNPBX/CFC/&lt;ID&gt;</c> and compares it with exactly 1, so
        /// an empty value — a switch nobody has ever flipped — and a 0 both fall through to the
        /// normal destination. The destinations are written by the shared helper (D36).
        /// </summary>
        [Fact]
        public void The_switch_context_reads_astdb_and_sends_the_call_one_way_or_the_other()
        {
            var control = SampleControls()[1];
            var context = Context(Render(SampleControls()), "cfc-1");

            var check = $" same => n,GotoIf($[\"${{DB(TNPBX/CFC/1)}}\" = \"{CallFlowControl.StateOn}\"]?" +
                $"{ExtensionsConfRenderer.CallFlowControlOverrideLabel})\n";
            var normal = DestinationDialplan.Lines(control.ToNormalDestination());
            var over = $" same => n({ExtensionsConfRenderer.CallFlowControlOverrideLabel}),NoOp(";
            var overLines = DestinationDialplan.Lines(control.ToOverrideDestination());

            Assert.Contains("exten => s,1,NoOp(Call flow control *28 Night mode)\n", context);
            Assert.Contains(check, context);
            Assert.Contains(normal, context);
            Assert.Contains(overLines, context);

            // Check, then the normal way straight after it, then the labelled override: anything
            // but 1 carries on past the GotoIf into the normal destination.
            var checkAt = context.IndexOf(check, StringComparison.Ordinal);
            var normalAt = context.IndexOf(normal, StringComparison.Ordinal);
            var overAt = context.IndexOf(over, StringComparison.Ordinal);
            var overLinesAt = context.IndexOf(overLines, overAt, StringComparison.Ordinal);

            Assert.True(checkAt < normalAt);
            Assert.True(normalAt < overAt);
            Assert.True(overAt < overLinesAt);
        }

        [Fact]
        public void The_normal_and_override_destinations_are_the_expected_gotos()
        {
            var context = Context(Render(SampleControls()), "cfc-1");

            Assert.Contains($" same => n,Goto({ExtensionsConfRenderer.InternalContext},1001,1)\n", context);
            Assert.Contains(" same => n,VoiceMail(1002@default,u)\n", context);
        }

        /// <summary>A switch handing on to another goes in through the other's door, never its toggle.</summary>
        [Fact]
        public void A_switch_can_hand_on_to_another_switch()
        {
            var context = Context(Render(SampleControls()), "cfc-2");

            Assert.Contains($" same => n,Goto({ExtensionsConfRenderer.CallFlowControlEntryContext},*28,1)\n", context);
            Assert.DoesNotContain($"Goto({ExtensionsConfRenderer.InternalContext},*28,1)", context);
        }

        [Fact]
        public void A_call_flow_control_destination_gotos_the_entry_context_not_the_toggle()
        {
            var steps = DestinationDialplan.Steps(new Destination(DestinationType.CallFlowControl, "*28"));

            Assert.Equal(new[] { $"Goto({ExtensionsConfRenderer.CallFlowControlEntryContext},*28,1)" }, steps);
        }

        [Theory]
        [InlineData("")]
        [InlineData("28")]
        [InlineData("*28,1)\n[evil")]
        public void A_call_flow_control_destination_that_is_not_a_code_is_never_written(string value)
        {
            Assert.Throws<InvalidOperationException>(() =>
                DestinationDialplan.Steps(new Destination(DestinationType.CallFlowControl, value)));
        }

        /// <summary>The door in: one entry per switch, by its code, on to the switch's own context.</summary>
        [Fact]
        public void The_entry_context_has_one_door_per_switch()
        {
            var entry = Context(Render(SampleControls()), ExtensionsConfRenderer.CallFlowControlEntryContext);

            Assert.Contains("exten => *28,1,Goto(cfc-1,s,1)\n", entry);
            Assert.Contains("exten => *271,1,Goto(cfc-2,s,1)\n", entry);
            Assert.Equal(2, entry.Split("exten => ").Length - 1);
        }

        /// <summary>
        /// Dialling the code from a phone flips the astdb key, moves the Custom: device the key
        /// lamps watch, and plays which way it went — all inside the internal context, where the
        /// phones dial.
        /// </summary>
        [Fact]
        public void Dialling_the_code_toggles_the_switch()
        {
            var internalContext = Context(Render(SampleControls()), ExtensionsConfRenderer.InternalContext);

            var expected =
                "exten => *28,1,Answer()\n" +
                " same => n,GotoIf($[\"${DB(TNPBX/CFC/1)}\" = \"1\"]?off)\n" +
                " same => n,Set(DB(TNPBX/CFC/1)=1)\n" +
                " same => n,Set(DEVICE_STATE(Custom:tnpbx-cfc-1)=INUSE)\n" +
                $" same => n,Playback({ExtensionsConfRenderer.CallFlowControlOnPrompt})\n" +
                " same => n,Hangup()\n" +
                " same => n(off),Set(DB(TNPBX/CFC/1)=0)\n" +
                " same => n,Set(DEVICE_STATE(Custom:tnpbx-cfc-1)=NOT_INUSE)\n" +
                $" same => n,Playback({ExtensionsConfRenderer.CallFlowControlOffPrompt})\n" +
                " same => n,Hangup()\n";

            Assert.Contains(expected, internalContext);
            Assert.Contains("exten => *271,1,Answer()\n", internalContext);
            Assert.Contains(" same => n,Set(DB(TNPBX/CFC/2)=1)\n", internalContext);
        }

        /// <summary>
        /// The hint is what a phone key subscribes to (D121), so it goes on the code in the
        /// internal context, alongside the parking slots' hints.
        /// </summary>
        [Fact]
        public void Every_switch_gets_a_hint_where_the_parking_slot_hints_are()
        {
            var actual = Render(SampleControls(), Parking());
            var internalContext = Context(actual, ExtensionsConfRenderer.InternalContext);

            var slotHint = internalContext.IndexOf($"exten => 2,hint,park:2@{ParkingConfRenderer.Context}\n", StringComparison.Ordinal);
            var firstHint = internalContext.IndexOf("exten => *28,hint,Custom:tnpbx-cfc-1\n", StringComparison.Ordinal);
            var secondHint = internalContext.IndexOf("exten => *271,hint,Custom:tnpbx-cfc-2\n", StringComparison.Ordinal);
            var pickup = internalContext.IndexOf($"exten => _{ExtensionsConfRenderer.PickupCode}.,", StringComparison.Ordinal);

            Assert.NotEqual(-1, slotHint);
            Assert.NotEqual(-1, firstHint);
            Assert.NotEqual(-1, secondHint);
            Assert.True(slotHint < firstHint);
            Assert.True(firstHint < secondHint);
            Assert.True(secondHint < pickup);
        }

        /// <summary>
        /// The state is Asterisk's, not the renderer's: nothing it is given says which way a switch
        /// is set, so the file cannot differ with it. The switch context carries both destinations
        /// and decides between them at call time.
        /// </summary>
        [Fact]
        public void The_file_is_the_same_whichever_way_the_switch_is_set()
        {
            var first = Render(SampleControls(), Parking());
            var second = Render(SampleControls(), Parking());

            Assert.Equal(first, second);

            var context = Context(first, "cfc-1");
            Assert.Contains("${DB(TNPBX/CFC/1)}", context);
            Assert.DoesNotContain("Set(DB(", context);
            Assert.DoesNotContain("DEVICE_STATE", context);
        }

        /// <summary>By code, shortest first, the way the catalog lists them.</summary>
        [Fact]
        public void Switches_render_in_code_order()
        {
            var order = ExtensionsConfRenderer.CallFlowControlRenderOrder(SampleControls());

            Assert.Equal(new[] { "*28", "*271" }, order.Select(c => c.FeatureCode));
        }

        /// <summary>
        /// A switch's context can never be a way out to the phone network: it includes nothing,
        /// so nothing in it can fall through to an outbound route (D50's rule, applied again).
        /// </summary>
        [Fact]
        public void A_switch_context_includes_nothing_and_dials_no_trunk()
        {
            var actual = Render(SampleControls());

            foreach (var name in new[] { "cfc-1", "cfc-2", ExtensionsConfRenderer.CallFlowControlEntryContext })
            {
                var context = Context(actual, name);
                Assert.DoesNotContain("include =>", context);
                Assert.DoesNotContain("Dial(PJSIP/", context);
            }
        }

        /// <summary>A row that reached the database some other way must not reach a conf file.</summary>
        [Theory]
        [InlineData("Night]\n[evil", "*28")]
        [InlineData("Night mode", "*28\nexten => 1")]
        [InlineData("Night mode", "28")]
        [InlineData("Night mode", "*28\n")]
        public void A_switch_that_would_not_validate_is_never_written(string name, string code)
        {
            var control = new CallFlowControl { CallFlowControlID = 1, Name = name, FeatureCode = code };

            Assert.Throws<InvalidOperationException>(() => Render(new[] { control }));
        }

        [Fact]
        public void A_switch_that_points_at_itself_is_never_written()
        {
            var control = new CallFlowControl
            {
                CallFlowControlID = 1,
                Name = "Night mode",
                FeatureCode = "*28",
                NormalDestinationType = "CallFlowControl",
                NormalDestinationValue = "*28",
            };

            Assert.Throws<InvalidOperationException>(() => Render(new[] { control }));
        }
    }
}
