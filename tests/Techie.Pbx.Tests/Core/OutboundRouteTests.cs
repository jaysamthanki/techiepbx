using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Outbound route validation. The international guard (D47) is the one that matters most:
    /// toll fraud is the reason a PBX gets attacked, and a route is the thing that would pay for
    /// it.
    /// </summary>
    public class OutboundRouteTests
    {
        private static OutboundRoute Valid() => new()
        {
            Name = "long-distance",
            DialPattern = "_1NXXXXXXXXX",
            TrunkID = 1,
            Priority = 10,
        };

        [Fact]
        public void A_route_filled_in_properly_validates()
        {
            Assert.Empty(Valid().Validate());
        }

        [Theory]
        [InlineData("_1NXXXXXXXXX", true)]
        [InlineData("_NXXXXXXX", true)]
        [InlineData("_911", true)]
        [InlineData("_1[2-9]XXXXXXXXX", true)]
        [InlineData("_18XX.", true)]
        [InlineData("_ZXXXXXX", true)]
        [InlineData("1NXXXXXXXXX", false)]     // no leading underscore
        [InlineData("_", false)]
        [InlineData("_1NXXX XXXX", false)]     // a space
        [InlineData("_1NXXXXXXXXX!", false)]   // ! matches early, which surprises people
        [InlineData("_1*XXXXXXX", false)]
        [InlineData("_1.XXXX", false)]         // a dot swallows the rest, so nothing may follow
        [InlineData("_1[2-9XXXX", false)]      // unclosed set
        [InlineData("_1[a-z]XXXX", false)]
        public void A_pattern_has_to_be_one_asterisk_would_understand(string pattern, bool valid)
        {
            var route = Valid();
            route.DialPattern = pattern;

            Assert.Equal(valid, route.Validate().Count == 0);
        }

        /// <summary>
        /// 011 and 00 are the international prefixes, and a wildcard first character reaches them
        /// too. There is no way to write a route that does (D47).
        /// </summary>
        [Theory]
        [InlineData("_011.")]
        [InlineData("_011XXXXXXXXXX")]
        [InlineData("_00.")]
        [InlineData("_0X.")]
        [InlineData("_0")]
        [InlineData("_X.")]
        [InlineData("_XXXXXXXXXX")]
        [InlineData("_.")]
        [InlineData("_[0-9].")]
        [InlineData("_[013].")]
        public void An_international_or_catch_all_route_cannot_be_created(string pattern)
        {
            var route = Valid();
            route.DialPattern = pattern;

            var errors = route.Validate();

            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Contains("international"));
        }

        /// <summary>
        /// The guard has to let ordinary dialling through, or it just gets switched off.
        /// </summary>
        [Theory]
        [InlineData("_1NXXXXXXXXX")]
        [InlineData("_NXXXXXXX")]
        [InlineData("_[2-9]XXXXXX")]
        [InlineData("_411")]
        [InlineData("_911")]
        public void Ordinary_dialling_is_not_caught_by_the_guard(string pattern)
        {
            Assert.Null(OutboundRoute.InternationalReason(pattern));
        }

        [Theory]
        [InlineData("local", true)]
        [InlineData("long-distance", true)]
        [InlineData("Route1", true)]
        [InlineData("1local", false)]
        [InlineData("-local", false)]
        [InlineData("local route", false)]
        [InlineData("local_route", false)]
        [InlineData("", false)]
        public void A_name_has_to_be_safe_as_a_context_name(string name, bool valid)
        {
            var route = Valid();
            route.Name = name;

            Assert.Equal(valid, route.Validate().Count == 0);
        }

        [Fact]
        public void The_context_is_the_name_with_a_prefix()
        {
            Assert.Equal("outbound-long-distance", Valid().Context);
        }

        [Fact]
        public void A_route_needs_a_trunk()
        {
            var route = Valid();
            route.TrunkID = 0;

            Assert.Contains("trunk", string.Join(" ", route.Validate()));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1000)]
        public void A_priority_outside_the_range_is_refused(int priority)
        {
            var route = Valid();
            route.Priority = priority;

            Assert.Contains("Priority", string.Join(" ", route.Validate()));
        }

        /// <summary>
        /// The underscore is added by the repository rather than demanded of the admin (D109),
        /// so this is what "NXXXXXX" becomes before validation ever sees it.
        /// </summary>
        [Theory]
        [InlineData("NXXXXXX", "_NXXXXXX")]
        [InlineData("  _1NXXXXXXXXX  ", "_1NXXXXXXXXX")]
        [InlineData("_911", "_911")]
        [InlineData("", "")]
        public void A_pattern_without_an_underscore_is_stored_with_one(string typed, string stored)
        {
            Assert.Equal(stored, OutboundRoute.NormalizePattern(typed));
        }

        /// <summary>
        /// A prepend is digits an admin chose, so the only guard it needs is the international
        /// one (D47 from the other side): 00 and 011 are prefixes, so a prepend starting with 0
        /// is a route around the pattern guard (D109).
        /// </summary>
        [Fact]
        public void A_prepend_starting_with_zero_is_refused()
        {
            var route = Valid();
            route.PrependDigits = "011";

            Assert.Contains("international", string.Join(" ", route.Validate()));
        }

        [Theory]
        [InlineData("1714")]
        [InlineData("1")]
        [InlineData("")]
        public void Ordinary_prepends_are_allowed(string prepend)
        {
            var route = Valid();
            route.PrependDigits = prepend;

            Assert.DoesNotContain("Prepend", string.Join(" ", route.Validate()));
        }

        [Theory]
        [InlineData("17 14")]
        [InlineData("17a14")]
        [InlineData("17141714171417")]
        public void A_prepend_has_to_be_digits(string prepend)
        {
            var route = Valid();
            route.PrependDigits = prepend;

            Assert.Contains("Prepend", string.Join(" ", route.Validate()));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(11)]
        public void A_strip_outside_the_range_is_refused(int strip)
        {
            var route = Valid();
            route.StripDigits = strip;

            Assert.Contains("Strip", string.Join(" ", route.Validate()));
        }

        [Fact]
        public void The_sent_number_is_the_prepend_in_front_of_the_stripped_digits()
        {
            var sevenLocal = Valid();
            sevenLocal.DialPattern = "_NXXXXXX";
            sevenLocal.PrependDigits = "1714";

            Assert.Equal("1714${EXTEN}", sevenLocal.SentNumberExpression);

            var dialNine = Valid();
            dialNine.DialPattern = "_9NXXXXXXXXX";
            dialNine.PrependDigits = "1";
            dialNine.StripDigits = 1;

            Assert.Equal("1${EXTEN:1}", dialNine.SentNumberExpression);
        }
    }
}
