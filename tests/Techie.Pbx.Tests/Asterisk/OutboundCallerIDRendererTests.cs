using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What an outbound call calls out as, and what its caller hears when the far side holds them
    /// (D125). Both renderers are here on purpose: the extension claims a caller ID on its endpoint
    /// in pjsip.conf and the outbound route contexts in extensions.conf are the only places that
    /// apply it, so the two halves are only correct together.
    /// </summary>
    public class OutboundCallerIDRendererTests
    {
        /// <summary>
        /// One extension with a caller ID typed as a name and number, one with a bare number, and one
        /// with none at all — the three cases pjsip.conf has to write, and the third is every
        /// extension on a system nobody has set this up on.
        /// </summary>
        private static List<Extension> SampleExtensions() => new()
        {
            new Extension
            {
                Number = "1001",
                Name = "Front Desk",
                Secret = "AAAAbbbbCCCCdddd1111",
                OutboundCallerID = "\"Front Desk\" <17771234500>",
            },
            new Extension
            {
                Number = "1002",
                Name = "Sales",
                Secret = "EEEEffffGGGGhhhh2222",
                OutboundCallerID = "17771234501",
            },
            new Extension { Number = "1003", Name = "No DID", Secret = "IIIIjjjjKKKKllll3333" },
        };

        private static List<MohClass> SampleMohClasses() => new()
        {
            new MohClass { MohClassID = 3, Name = "Front Desk", Directory = "front-desk" },
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
        };

        /// <summary>
        /// One route with a caller ID and a class of its own, one with neither — so a golden file
        /// shows both halves of both choices.
        /// </summary>
        private static List<OutboundRoute> SampleRoutes() => new()
        {
            new OutboundRoute
            {
                OutboundRouteID = 1,
                Name = "local",
                DialPattern = "_NXXXXXX",
                PrependDigits = "1714",
                TrunkID = 1,
                Priority = 10,
                CallerID = "\"Acme Sales\" <17771234567>",
                MohClassID = 3,
            },
            new OutboundRoute
            {
                OutboundRouteID = 2,
                Name = "long-distance",
                DialPattern = "_1NXXXXXXXXX",
                TrunkID = 1,
                Priority = 20,
            },
        };

        private static PjsipTransport NatTransport() => new()
        {
            LocalNets = { "10.8.20.0/24" },
            ExternalAddress = "203.0.113.10",
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        /// <summary>The two extensions the dialplan golden is rendered with: one claim, one not.</summary>
        private static List<Extension> DialplanExtensions() =>
            SampleExtensions().Where(e => e.Number != "1003").ToList();

        private static string Render(List<Extension> extensions, List<OutboundRoute> routes, List<MohClass> mohClasses) =>
            ExtensionsConfRenderer.Render(
                extensions,
                SampleTrunks(),
                routes,
                new List<InboundRoute>(),
                new List<RingGroup>(),
                new List<Announcement>(),
                new List<Ivr>(),
                new List<TimeCondition>(),
                AsteriskSettings.DefaultTimezone,
                new ParkingSettings(),
                mohClasses);

        /// <summary>The text of one context, from its heading to the next one.</summary>
        private static string Context(string dialplan, string name)
        {
            var start = dialplan.IndexOf($"[{name}]", StringComparison.Ordinal);
            var next = dialplan.IndexOf("\n[", start + 1, StringComparison.Ordinal);

            return next < 0 ? dialplan[start..] : dialplan[start..next];
        }

        [Fact]
        public void An_extension_claim_is_a_set_var_on_its_own_endpoint()
        {
            var actual = PjsipConfRenderer.Render(NatTransport(), SampleExtensions());

            Assert.Equal(Expected("pjsip-outbound-cid.conf"), actual);
        }

        /// <summary>
        /// The endpoint's own <c>callerid</c> is the extension's identity on an internal call, and the
        /// claim is a channel variable beside it — not a replacement for it. A colleague still sees
        /// the name and the extension number.
        /// </summary>
        [Fact]
        public void The_claim_does_not_touch_what_an_internal_call_shows()
        {
            var actual = PjsipConfRenderer.Render(NatTransport(), SampleExtensions());

            Assert.Contains("callerid = \"Front Desk\" <1001>\nset_var = TNPBX_CID=\"Front Desk\" <17771234500>\n", actual);
            Assert.Contains("callerid = \"Sales\" <1002>\nset_var = TNPBX_CID=17771234501\n", actual);
        }

        /// <summary>
        /// An extension with no caller ID of its own gets no <c>set_var</c> at all, which is every
        /// extension until somebody gives one a DID — so every pjsip.conf this system has ever
        /// written is unchanged.
        /// </summary>
        [Fact]
        public void An_extension_with_no_caller_id_of_its_own_gets_nothing()
        {
            var plain = SampleExtensions().Where(e => e.Number == "1003").ToList();

            Assert.DoesNotContain("set_var", PjsipConfRenderer.Render(NatTransport(), plain));
        }

        [Fact]
        public void Route_caller_id_and_music_match_expected_file()
        {
            Assert.Equal(
                Expected("extensions-outbound-cid.conf"),
                Render(DialplanExtensions(), SampleRoutes(), SampleMohClasses()));
        }

        /// <summary>
        /// The guard, which is the whole precedence order in one line: the route's caller ID is set
        /// only when the extension claimed nothing, so an extension with a DID of its own keeps it.
        /// Nothing anywhere sets <c>CALLERID(all)</c> unguarded.
        /// </summary>
        [Fact]
        public void A_route_caller_id_never_clobbers_an_extension_claim()
        {
            var context = Context(Render(DialplanExtensions(), SampleRoutes(), SampleMohClasses()), "outbound-local");

            Assert.Contains(
                "exten => _NXXXXXX,1,ExecIf($[\"${TNPBX_CID}\" != \"\"]?Set(CALLERID(all)=${TNPBX_CID}))\n" +
                " same => n,ExecIf($[\"${TNPBX_CID}\" = \"\"]?Set(CALLERID(all)=\"Acme Sales\" <17771234567>))\n",
                context);

            // Every caller ID in the dialplan is set inside an ExecIf, in one direction or the other.
            var actual = Render(DialplanExtensions(), SampleRoutes(), SampleMohClasses());
            var sets = actual.Split('\n').Where(line => line.Contains("CALLERID(all)=", StringComparison.Ordinal)).ToList();

            Assert.NotEmpty(sets);
            Assert.All(sets, line => Assert.Contains("ExecIf(", line, StringComparison.Ordinal));
        }

        /// <summary>
        /// The claim is applied in the outbound route contexts and nowhere else. An internal call
        /// never enters one, and neither does a call that arrived on a trunk — a trunk's context
        /// includes nothing (D50) — so neither can have its caller ID rewritten by this.
        /// </summary>
        [Fact]
        public void The_claim_is_applied_in_the_route_contexts_only()
        {
            var actual = Render(DialplanExtensions(), SampleRoutes(), SampleMohClasses());

            Assert.DoesNotContain("CALLERID", Context(actual, "internal"));
            Assert.DoesNotContain("CALLERID", Context(actual, "from-trunk-callcentric"));
            Assert.Contains("CALLERID", Context(actual, "outbound-local"));
            Assert.Contains("CALLERID", Context(actual, "outbound-long-distance"));
        }

        /// <summary>
        /// No extension claims anything, so there is nothing to apply and the line that would apply
        /// it is not written. The route's own caller ID is still guarded: the variable is simply
        /// empty, so the ExecIf always fires, and the line does not change meaning on the day
        /// somebody gives an extension a DID.
        /// </summary>
        [Fact]
        public void No_extension_claim_means_no_line_applying_one()
        {
            var plain = new List<Extension>
            {
                new() { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
            };

            var context = Context(Render(plain, SampleRoutes(), SampleMohClasses()), "outbound-local");

            Assert.DoesNotContain("!= \"\"", context);
            Assert.Contains(
                "exten => _NXXXXXX,1,ExecIf($[\"${TNPBX_CID}\" = \"\"]?Set(CALLERID(all)=\"Acme Sales\" <17771234567>))\n",
                context);
        }

        /// <summary>
        /// A route with neither a caller ID nor a class, on a system where no extension claims one,
        /// renders exactly the one Dial line and one Hangup it rendered before any of this existed —
        /// which is why every other golden file in this suite is unchanged.
        /// </summary>
        [Fact]
        public void A_route_that_names_nothing_renders_what_it_always_did()
        {
            var plain = new List<Extension>
            {
                new() { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
            };

            var routes = new List<OutboundRoute>
            {
                new() { OutboundRouteID = 2, Name = "long-distance", DialPattern = "_1NXXXXXXXXX", TrunkID = 1, Priority = 20 },
            };

            var context = Context(Render(plain, routes, SampleMohClasses()), "outbound-long-distance");

            Assert.Equal(
                "[outbound-long-distance]\n" +
                "; long-distance (20) out over callcentric\n" +
                "exten => _1NXXXXXXXXX,1,Dial(PJSIP/callcentric/sip:${EXTEN}@callcentric.com,60,tTkK)\n" +
                " same => n,Hangup()\n",
                context);
        }

        /// <summary>
        /// The class the outbound caller hears, set unguarded and before the Dial: nothing has ever
        /// named a class on this channel — an outbound call passes no trunk context and no extension
        /// entry — so there is nothing here to avoid clobbering (D125).
        /// </summary>
        [Fact]
        public void The_route_class_is_set_before_the_call_is_dialled()
        {
            var context = Context(Render(DialplanExtensions(), SampleRoutes(), SampleMohClasses()), "outbound-local");

            Assert.Contains(
                " same => n,Set(CHANNEL(musicclass)=Front Desk)\n" +
                " same => n,Dial(PJSIP/callcentric/sip:1714${EXTEN}@callcentric.com,60,tTkK)\n",
                context);
        }

        [Fact]
        public void A_route_with_no_class_of_its_own_sets_none()
        {
            var context = Context(Render(DialplanExtensions(), SampleRoutes(), SampleMohClasses()), "outbound-long-distance");

            Assert.DoesNotContain("musicclass", context);
        }

        /// <summary>
        /// A class that is not there is refused rather than written as a name Asterisk would find
        /// nothing for — the same answer the inbound routes give. The repository stops this being
        /// stored, and deleting a class puts the routes that named it back to none.
        /// </summary>
        [Fact]
        public void A_class_the_renderer_was_not_given_is_refused()
        {
            Assert.Throws<InvalidOperationException>(
                () => Render(DialplanExtensions(), SampleRoutes(), new List<MohClass>()));
        }

        /// <summary>
        /// And a class called <c>default</c> never reaches the dialplan, in either direction: that is
        /// the name Asterisk keeps for its own fallback, which is what this system's silence is made
        /// of (D119).
        /// </summary>
        [Fact]
        public void A_class_named_default_never_reaches_an_outbound_route()
        {
            var classes = new List<MohClass>
            {
                new() { MohClassID = 3, Name = "default", Directory = "front-desk" },
            };

            Assert.Throws<InvalidOperationException>(() => Render(DialplanExtensions(), SampleRoutes(), classes));
        }

        /// <summary>
        /// A row that reached the database another way cannot open a section or comment out the rest
        /// of the file through either caller ID field. The route's is refused by validation before the
        /// renderer sees it; the extension's is refused the same way, and by
        /// <c>ConfText</c> after that.
        /// </summary>
        [Theory]
        [InlineData("\"evil\n[evil]\" <100>")]
        [InlineData("evil; comment <100>")]
        [InlineData("\"evil\" <100>; comment")]
        [InlineData("100,200")]
        public void Injection_through_a_route_caller_id_is_refused(string callerID)
        {
            var routes = SampleRoutes();
            routes[0].CallerID = callerID;

            Assert.Throws<InvalidOperationException>(() => Render(DialplanExtensions(), routes, SampleMohClasses()));
        }

        [Theory]
        [InlineData("\"evil\n[evil]\" <100>")]
        [InlineData("evil; comment <100>")]
        [InlineData("100,200")]
        public void Injection_through_an_extension_caller_id_is_refused(string callerID)
        {
            var extensions = DialplanExtensions();
            extensions[0].OutboundCallerID = callerID;

            Assert.Throws<InvalidOperationException>(
                () => PjsipConfRenderer.Render(NatTransport(), extensions));
        }
    }
}
