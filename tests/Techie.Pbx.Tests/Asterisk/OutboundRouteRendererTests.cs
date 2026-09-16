using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What outbound routes turn into: a context per route, included in the order they are tried,
    /// and a last context that stops everything else (D45, D46).
    /// </summary>
    public class OutboundRouteRendererTests
    {
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
        };

        private static List<OutboundRoute> SampleRoutes() => new()
        {
            // Out of order on purpose, plus one that is switched off and one whose trunk is gone.
            new OutboundRoute { OutboundRouteID = 2, Name = "long-distance", DialPattern = "_1NXXXXXXXXX", TrunkID = 1, Priority = 20 },
            new OutboundRoute { OutboundRouteID = 3, Name = "switched-off", DialPattern = "_2XXXXXX", TrunkID = 1, Priority = 5, Enabled = false },
            new OutboundRoute { OutboundRouteID = 4, Name = "orphan", DialPattern = "_3XXXXXX", TrunkID = 99, Priority = 1 },
            new OutboundRoute { OutboundRouteID = 1, Name = "local", DialPattern = "_NXXXXXXX", TrunkID = 1, Priority = 10 },
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        private static string Render(params OutboundRoute[] routes) =>
            ExtensionsConfRenderer.Render(new List<Extension>(), SampleTrunks(), routes);

        [Fact]
        public void Routes_match_expected_file()
        {
            var actual = ExtensionsConfRenderer.Render(new List<Extension>(), SampleTrunks(), SampleRoutes());

            Assert.Equal(Expected("extensions-routes.conf"), actual);
        }

        /// <summary>
        /// A system with no routes has to render exactly what it rendered before routes existed:
        /// no outbound context, no include, and above all no catch-all pattern that did not used
        /// to be there.
        /// </summary>
        [Fact]
        public void A_system_with_no_routes_renders_exactly_what_it_did_before()
        {
            var extensions = new List<Extension>
            {
                new() { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
            };

            var withoutRoutes = ExtensionsConfRenderer.Render(extensions, SampleTrunks(), new List<OutboundRoute>());

            Assert.Equal(ExtensionsConfRenderer.Render(extensions, SampleTrunks()), withoutRoutes);
            Assert.DoesNotContain("include =>", withoutRoutes);
            Assert.DoesNotContain(ExtensionsConfRenderer.OutboundContext, withoutRoutes);
            Assert.DoesNotContain(ExtensionsConfRenderer.BlockedContext, withoutRoutes);
        }

        /// <summary>
        /// Asterisk searches a context's own extensions before anything it includes, so a route
        /// pattern can never take a call meant for a phone or a feature code (D46).
        /// </summary>
        [Fact]
        public void The_outbound_include_comes_after_the_internal_numbers()
        {
            var extensions = new List<Extension>
            {
                new() { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
            };

            var actual = ExtensionsConfRenderer.Render(extensions, SampleTrunks(), SampleRoutes());
            var include = actual.IndexOf($"include => {ExtensionsConfRenderer.OutboundContext}", StringComparison.Ordinal);

            Assert.True(include > actual.IndexOf("exten => 1001,", StringComparison.Ordinal));
            Assert.True(include > actual.IndexOf("exten => *43,", StringComparison.Ordinal));
        }

        /// <summary>
        /// Includes are searched in the order they are written, which is the only reason "first
        /// match wins" means our priority order rather than Asterisk's own pattern ranking.
        /// </summary>
        [Fact]
        public void Routes_are_included_in_priority_order_whatever_order_they_arrive_in()
        {
            var actual = ExtensionsConfRenderer.Render(new List<Extension>(), SampleTrunks(), SampleRoutes());
            var outbound = actual[actual.IndexOf("[outbound]\n", StringComparison.Ordinal)..];

            Assert.StartsWith(
                "[outbound]\ninclude => outbound-local\ninclude => outbound-long-distance\ninclude => outbound-blocked\n",
                outbound);
        }

        [Fact]
        public void The_blocked_context_is_always_included_last()
        {
            var actual = Render(SampleRoutes()[0]);
            var blocked = actual.IndexOf($"include => {ExtensionsConfRenderer.BlockedContext}", StringComparison.Ordinal);

            Assert.True(blocked > actual.IndexOf("include => outbound-long-distance", StringComparison.Ordinal));
        }

        /// <summary>Toll fraud starts with a number nobody meant to allow (D45).</summary>
        [Fact]
        public void A_number_that_matches_no_route_does_not_go_out()
        {
            var actual = Render(SampleRoutes()[0]);

            Assert.Contains($"[{ExtensionsConfRenderer.BlockedContext}]\n", actual);
            Assert.Contains("exten => _X.,1,NoOp(No outbound route for ${EXTEN})\n", actual);
            Assert.Contains(" same => n,Hangup()\n", actual);

            // The catch-all must not be able to reach a trunk.
            var blocked = actual[actual.IndexOf($"[{ExtensionsConfRenderer.BlockedContext}]", StringComparison.Ordinal)..];
            Assert.DoesNotContain("Dial(", blocked);
        }

        [Fact]
        public void A_switched_off_route_is_not_in_the_config_at_all()
        {
            var actual = ExtensionsConfRenderer.Render(new List<Extension>(), SampleTrunks(), SampleRoutes());

            Assert.DoesNotContain("switched-off", actual);
            Assert.DoesNotContain("_2XXXXXX", actual);
        }

        /// <summary>
        /// A route whose trunk was deleted or disabled would dial a PJSIP endpoint that is not
        /// there. It is left out rather than written and left to fail at call time.
        /// </summary>
        [Fact]
        public void A_route_whose_trunk_is_gone_is_left_out()
        {
            var actual = ExtensionsConfRenderer.Render(new List<Extension>(), SampleTrunks(), SampleRoutes());

            Assert.DoesNotContain("orphan", actual);
            Assert.DoesNotContain("_3XXXXXX", actual);
        }

        [Fact]
        public void A_route_dials_its_own_trunk_with_the_number_that_was_dialled()
        {
            Assert.Contains(
                "exten => _NXXXXXXX,1,Dial(PJSIP/callcentric/sip:${EXTEN}@callcentric.com,60)\n same => n,Hangup()\n",
                Render(SampleRoutes()[3]));
        }

        [Fact]
        public void A_route_that_would_not_validate_is_never_written()
        {
            var route = new OutboundRoute { Name = "broken", DialPattern = "_011.", TrunkID = 1, Priority = 1 };

            Assert.Throws<InvalidOperationException>(() => Render(route));
        }

        /// <summary>
        /// A row that reached the database without going through the repository still cannot open
        /// a section or comment out the rest of the file.
        /// </summary>
        [Theory]
        [InlineData("evil\n[evil]\nexten => _X.,1,Dial(PJSIP/callcentric/${EXTEN})")]
        [InlineData("evil; comment")]
        [InlineData("evil]")]
        public void Injection_through_a_route_name_is_refused(string name)
        {
            var route = new OutboundRoute { Name = name, DialPattern = "_1NXXXXXXXXX", TrunkID = 1, Priority = 1 };

            Assert.Throws<InvalidOperationException>(() => Render(route));
        }
    }
}
