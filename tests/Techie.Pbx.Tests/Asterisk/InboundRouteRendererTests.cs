using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What inbound routes turn into: entries in the trunk's own context, sent on by the shared
    /// destination helper, and something safe for every number nothing claimed (D49, D50).
    /// </summary>
    public class InboundRouteRendererTests
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

        private static List<Trunk> SampleTrunks() => new()
        {
            new Trunk
            {
                TrunkID = 1,
                Name = "callcentric",
                ServerHost = "callcentric.com",
                Username = "17771234567",
                Password = "not-a-real-password",
                Register = true,
            },
            new Trunk
            {
                TrunkID = 2,
                Name = "ip-provider",
                ServerHost = "sip.example.net",
                Register = false,
            },
        };

        private static List<InboundRoute> SampleRoutes() => new()
        {
            // Out of order on purpose, plus one switched off and one for a trunk that is not here.
            new InboundRoute
            {
                InboundRouteID = 3,
                TrunkID = 1,
                CatchAll = true,
                DestinationType = "Hangup",
                Description = "",
            },
            new InboundRoute
            {
                InboundRouteID = 2,
                TrunkID = 1,
                DID = "17771234568",
                DestinationType = "Voicemail",
                DestinationValue = "1002",
                Description = "Sales after hours",
            },
            new InboundRoute
            {
                InboundRouteID = 4,
                TrunkID = 1,
                DID = "17771234569",
                DestinationType = "Extension",
                DestinationValue = "1001",
                Enabled = false,
            },
            new InboundRoute
            {
                InboundRouteID = 5,
                TrunkID = 99,
                DID = "17771234500",
                DestinationType = "Extension",
                DestinationValue = "1001",
            },
            new InboundRoute
            {
                InboundRouteID = 1,
                TrunkID = 1,
                DID = "17771234567",
                DestinationType = "Extension",
                DestinationValue = "1001",
                Description = "Main line",
            },
            // The two a caller off the street most often wants first: the clock, and the menu
            // (D35 amendment). 500 and 600 are the play extensions the IVR and time-condition
            // goldens use, so it is the same door in both places.
            new InboundRoute
            {
                InboundRouteID = 6,
                TrunkID = 1,
                DID = "17771234570",
                DestinationType = "Ivr",
                DestinationValue = "500",
                Description = "Main menu",
            },
            new InboundRoute
            {
                InboundRouteID = 7,
                TrunkID = 1,
                DID = "17771234571",
                DestinationType = "TimeCondition",
                DestinationValue = "600",
                Description = "Business hours",
            },
        };

        /// <summary>
        /// The class that ships and one a site added, which is the case worth rendering: a route
        /// that names the shipped class and a route that names another are the same code path, and
        /// a route that names neither has to write nothing at all (D122 amended).
        /// </summary>
        private static List<MohClass> SampleMohClasses() => new()
        {
            new MohClass { MohClassID = 1, Name = "Standard", Directory = "default", IsDefault = true },
            new MohClass { MohClassID = 3, Name = "Front Desk", Directory = "front-desk" },
        };

        /// <summary>
        /// One trunk, one DID with music of its own, one DID with none, and a catch-all with the
        /// class that ships — so the golden file shows both halves of the choice and both places
        /// the line can be written.
        /// </summary>
        private static List<InboundRoute> MohRoutes() => new()
        {
            new InboundRoute
            {
                InboundRouteID = 1,
                TrunkID = 1,
                DID = "17771234567",
                DestinationType = "Extension",
                DestinationValue = "1001",
                Description = "Main line",
                MohClassID = 3,
            },
            new InboundRoute
            {
                InboundRouteID = 2,
                TrunkID = 1,
                DID = "17771234568",
                DestinationType = "Voicemail",
                DestinationValue = "1002",
                Description = "Sales after hours",
            },
            new InboundRoute
            {
                InboundRouteID = 3,
                TrunkID = 1,
                CatchAll = true,
                DestinationType = "Extension",
                DestinationValue = "1002",
                Description = "Everything else",
                MohClassID = 1,
            },
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        /// <summary>The same render every music on hold test does: routes, and the classes they name.</summary>
        private static string RenderWithMusic(List<InboundRoute> routes, List<MohClass> mohClasses) =>
            ExtensionsConfRenderer.Render(
                SampleExtensions(),
                SampleTrunks(),
                new List<OutboundRoute>(),
                routes,
                new List<RingGroup>(),
                new List<Announcement>(),
                new List<Ivr>(),
                new List<TimeCondition>(),
                AsteriskSettings.DefaultTimezone,
                new ParkingSettings(),
                mohClasses);

        private static string Render(params InboundRoute[] routes) =>
            ExtensionsConfRenderer.Render(SampleExtensions(), SampleTrunks(), new List<OutboundRoute>(), routes);

        [Fact]
        public void Inbound_routes_match_expected_file()
        {
            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), SampleTrunks(), new List<OutboundRoute>(), SampleRoutes());

            Assert.Equal(Expected("extensions-inbound.conf"), actual);
        }

        /// <summary>
        /// A trunk with no routes still needs somewhere for its calls to land, and it has to be the
        /// same somewhere it was before inbound routes existed (D50).
        /// </summary>
        [Fact]
        public void A_trunk_with_no_routes_hangs_up_as_it_always_did()
        {
            var actual = ExtensionsConfRenderer.Render(new List<Extension>(), SampleTrunks(), new List<OutboundRoute>());

            Assert.Equal(
                ExtensionsConfRenderer.Render(new List<Extension>(), SampleTrunks(), new List<OutboundRoute>(), new List<InboundRoute>()),
                actual);
            Assert.Contains("exten => _X.,1,Set(DID=${CUT(CUT(PJSIP_HEADER(read,To),@,1),:,2)})\n", actual);
            Assert.Contains(" same => n(none),NoOp(No inbound route for ${DID} on callcentric)\n same => n,Hangup()\n", actual);
        }

        /// <summary>A system with no trunks has no inbound anything, and never did.</summary>
        [Fact]
        public void A_system_with_no_trunks_renders_exactly_what_it_did_before()
        {
            var withRoutes = ExtensionsConfRenderer.Render(
                SampleExtensions(), new List<Trunk>(), new List<OutboundRoute>(), SampleRoutes());

            Assert.Equal(ExtensionsConfRenderer.Render(SampleExtensions()), withRoutes);
        }

        [Fact]
        public void A_did_is_sent_where_its_destination_says()
        {
            var actual = Render(SampleRoutes()[4]);

            Assert.Contains(" same => n,GotoIf($[\"${DID}\" = \"17771234567\"]?r1)\n", actual);
            Assert.Contains(" same => n(r1),NoOp(Inbound 17771234567 on callcentric to Extension:1001)\n", actual);
            Assert.Contains(" same => n,Goto(internal,1001,1)\n", actual);
        }

        [Fact]
        public void A_did_can_go_straight_to_a_mailbox()
        {
            var actual = Render(SampleRoutes()[1]);

            Assert.Contains(" same => n,VoiceMail(1002@default,u)\n same => n,Hangup()\n", actual);
        }

        [Fact]
        public void A_did_can_go_to_an_ivr()
        {
            var actual = Render(SampleRoutes()[5]);

            Assert.Contains(" same => n(r6),NoOp(Inbound 17771234570 on callcentric to Ivr:500)\n", actual);
            Assert.Contains(" same => n,Goto(internal,500,1)\n", actual);
        }

        [Fact]
        public void A_did_can_go_to_a_time_condition()
        {
            var actual = Render(SampleRoutes()[6]);

            Assert.Contains(" same => n(r7),NoOp(Inbound 17771234571 on callcentric to TimeCondition:600)\n", actual);
            Assert.Contains(" same => n,Goto(internal,600,1)\n", actual);
        }

        /// <summary>
        /// The point of routing an inbound call through the internal context: the Goto the route
        /// writes lands on the entry the IVR and time-condition renderers write for themselves, so
        /// a caller off the street reaches the menu and the clock by the door an internal caller
        /// uses (D36, D35 amendment). Rendering both together is what proves the two halves agree.
        /// </summary>
        [Fact]
        public void An_ivr_and_a_time_condition_route_land_on_the_entry_that_is_really_there()
        {
            var announcements = new List<Announcement>
            {
                new() { AnnouncementID = 2, Name = "Menu greeting", AudioFile = "menu-greeting.wav" },
            };
            var ivrs = new List<Ivr>
            {
                new()
                {
                    IvrID = 1,
                    Name = "Main menu",
                    AnnouncementID = 2,
                    PlayExtension = "500",
                    Entries = new List<IvrEntry>
                    {
                        new() { Digit = "1", DestinationType = "Extension", DestinationValue = "1001" },
                    },
                },
            };
            var conditions = new List<TimeCondition>
            {
                // No rules, so it is always closed — which is all this test needs it to be: what
                // matters here is that the number has an entry to arrive at.
                new() { TimeConditionID = 1, Name = "Business hours", PlayExtension = "600" },
            };

            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(),
                SampleTrunks(),
                new List<OutboundRoute>(),
                SampleRoutes(),
                new List<RingGroup>(),
                announcements,
                ivrs,
                conditions);

            Assert.Contains("exten => 500,1,Goto(ivr-1,s,1)\n", Context(actual, "internal"));
            Assert.Contains("exten => 600,1,Goto(tc-1,s,1)\n", Context(actual, "internal"));
            Assert.Contains(" same => n,Goto(internal,500,1)\n", Context(actual, "from-trunk-callcentric"));
            Assert.Contains(" same => n,Goto(internal,600,1)\n", Context(actual, "from-trunk-callcentric"));
        }

        /// <summary>
        /// The DID entries are literal extensions and the catch-all is a pattern, and Asterisk
        /// always prefers a literal, so the catch-all cannot take a call a DID route claimed (D49).
        /// </summary>
        [Fact]
        public void A_catch_all_takes_the_rest_and_replaces_the_hangup()
        {
            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), SampleTrunks(), new List<OutboundRoute>(), SampleRoutes());
            var context = Context(actual, "from-trunk-callcentric");

            Assert.Contains(" same => n(none),NoOp(Inbound catch-all on callcentric to Hangup)\n", context);
            Assert.DoesNotContain("No inbound route for", context);
            Assert.Contains("GotoIf($[\"${DID}\" = \"17771234567\"]?r1)", context);
        }

        [Fact]
        public void A_catch_all_on_one_trunk_leaves_the_other_alone()
        {
            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), SampleTrunks(), new List<OutboundRoute>(), SampleRoutes());

            Assert.Contains("No inbound route for ${DID} on ip-provider", Context(actual, "from-trunk-ip-provider"));
        }

        [Fact]
        public void A_switched_off_route_is_not_in_the_config_at_all()
        {
            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), SampleTrunks(), new List<OutboundRoute>(), SampleRoutes());

            Assert.DoesNotContain("17771234569", actual);
        }

        [Fact]
        public void A_route_for_a_trunk_that_is_gone_is_left_out()
        {
            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), SampleTrunks(), new List<OutboundRoute>(), SampleRoutes());

            Assert.DoesNotContain("17771234500", actual);
        }

        /// <summary>
        /// Whatever happens, an inbound call must not end up somewhere that can dial out: that is
        /// how a PBX becomes someone else's long distance carrier.
        /// </summary>
        [Fact]
        public void An_inbound_context_never_dials_a_trunk()
        {
            var actual = ExtensionsConfRenderer.Render(
                SampleExtensions(), SampleTrunks(), new List<OutboundRoute>(), SampleRoutes());

            Assert.DoesNotContain("Dial(PJSIP/callcentric", Context(actual, "from-trunk-callcentric"));
            Assert.DoesNotContain("include =>", Context(actual, "from-trunk-callcentric"));
        }

        [Fact]
        public void A_route_that_would_not_validate_is_never_written()
        {
            var route = new InboundRoute { TrunkID = 1, DID = "not-digits", DestinationType = "Hangup" };

            Assert.Throws<InvalidOperationException>(() => Render(route));
        }

        /// <summary>
        /// A row that reached the database another way still cannot open a section of its own or
        /// comment out what follows.
        /// </summary>
        [Theory]
        [InlineData("Evil\n[evil]\nexten => _X.,1,Dial(PJSIP/callcentric/sip:1@x)")]
        [InlineData("Evil; comment")]
        [InlineData("Evil]")]
        public void Injection_through_a_description_is_refused(string description)
        {
            var route = new InboundRoute
            {
                TrunkID = 1,
                DID = "17771234567",
                DestinationType = "Extension",
                DestinationValue = "1001",
                Description = description,
            };

            Assert.Throws<InvalidOperationException>(() => Render(route));
        }

        /// <summary>
        /// The music a caller on this route hears while they are held (D122 amended): one
        /// <c>Set(CHANNEL(musicclass)=...)</c> in the trunk's context, written before the call is
        /// handed on so it is already in place whoever holds them.
        /// </summary>
        [Fact]
        public void Music_on_hold_per_route_matches_expected_file()
        {
            Assert.Equal(Expected("extensions-inbound-moh.conf"), RenderWithMusic(MohRoutes(), SampleMohClasses()));
        }

        /// <summary>
        /// The line goes in immediately before the destination, not after it: a Goto never comes
        /// back, so anything written after it would be a line Asterisk reaches on no call at all.
        /// </summary>
        [Fact]
        public void The_class_is_set_before_the_call_is_handed_on()
        {
            var context = Context(RenderWithMusic(MohRoutes(), SampleMohClasses()), "from-trunk-callcentric");

            Assert.Contains(
                " same => n(r1),NoOp(Inbound 17771234567 on callcentric to Extension:1001)\n" +
                " same => n,Set(CHANNEL(musicclass)=Front Desk)\n" +
                " same => n,Goto(internal,1001,1)\n",
                context);
        }

        /// <summary>A catch-all is a route like any other, and it may have music of its own.</summary>
        [Fact]
        public void A_catch_all_can_name_a_class_too()
        {
            var context = Context(RenderWithMusic(MohRoutes(), SampleMohClasses()), "from-trunk-callcentric");

            Assert.Contains(
                " same => n(none),NoOp(Inbound catch-all on callcentric to Extension:1002)\n" +
                " same => n,Set(CHANNEL(musicclass)=Standard)\n",
                context);
        }

        /// <summary>
        /// A route that names no class writes no line, which leaves the channel exactly as every
        /// route left it before the column existed. That is what makes this change nothing at all
        /// for a system nobody has configured it on.
        /// </summary>
        [Fact]
        public void A_route_with_no_class_of_its_own_sets_nothing()
        {
            Assert.DoesNotContain("musicclass", RenderWithMusic(SampleRoutes(), SampleMohClasses()));
            Assert.Equal(
                Expected("extensions-inbound.conf"),
                RenderWithMusic(SampleRoutes(), SampleMohClasses()));
        }

        /// <summary>
        /// A class that is not there is refused rather than written as a name Asterisk would find
        /// nothing for — the same answer a dangling destination gets. The repository stops this
        /// from being stored, and deleting a class puts the routes that named it back to none, so
        /// reaching here means a row arrived another way.
        /// </summary>
        [Fact]
        public void A_class_the_renderer_was_not_given_is_refused()
        {
            Assert.Throws<InvalidOperationException>(() => RenderWithMusic(MohRoutes(), new List<MohClass>()));
        }

        /// <summary>
        /// And a class whose name Asterisk could not match is refused even though it is in the
        /// list: <c>default</c> is the fallback that D119's silence depends on, and the renderers
        /// check it wherever a class name is written.
        /// </summary>
        [Fact]
        public void A_class_named_default_never_reaches_the_dialplan()
        {
            var classes = new List<MohClass>
            {
                new() { MohClassID = 1, Name = "Standard", Directory = "default", IsDefault = true },
                new() { MohClassID = 3, Name = "default", Directory = "front-desk" },
            };

            Assert.Throws<InvalidOperationException>(() => RenderWithMusic(MohRoutes(), classes));
        }

        /// <summary>The text of one context, from its heading to the next one.</summary>
        private static string Context(string dialplan, string name)
        {
            var start = dialplan.IndexOf($"[{name}]", StringComparison.Ordinal);
            var next = dialplan.IndexOf("\n[", start + 1, StringComparison.Ordinal);

            return next < 0 ? dialplan[start..] : dialplan[start..next];
        }
    }
}
