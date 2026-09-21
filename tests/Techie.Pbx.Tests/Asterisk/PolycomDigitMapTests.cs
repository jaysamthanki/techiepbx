using System.Text;
using System.Text.RegularExpressions;
using Techie.Pbx.Asterisk.Provisioning;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The digit map a Polycom phone dials by (D124). The bug this is about was real and visible:
    /// the old fixed map led with <c>xxxx</c>, which full-matches at exactly four digits, so the
    /// phone sent an attended-transfer target off to Asterisk as soon as four digits of a ten-digit
    /// mobile number had been typed.
    ///
    /// So the tests here are not only the golden string. <see cref="SendsEarly"/> compiles the map
    /// the way the phone reads it and asks the question that matters: is there a prefix of this
    /// number that the phone would dial on its own? Every pattern that could be the start of
    /// something longer has to answer no.
    /// </summary>
    public class PolycomDigitMapTests
    {
        /// <summary>The map before this piece, kept to prove the test can see the bug.</summary>
        private const string OldMap = "xxxx|*xx.T|[2-9]11|0T";

        /// <summary>The lab's extensions: 100 to 104, three digits, all starting with 1.</summary>
        private static List<Extension> LabExtensions() =>
            Enumerable.Range(100, 5)
                .Select(number => new Extension
                {
                    Number = number.ToString(),
                    Name = $"Desk {number}",
                    Secret = "AAAAbbbbCCCCdddd1111",
                })
                .ToList();

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).Trim();

        /// <summary>
        /// One pattern as the phone reads it: whether it waits for the inter-digit timeout, and
        /// what it matches. <c>x</c> is any digit, <c>.</c> repeats the element before it, and a
        /// trailing <c>T</c> is the wait.
        /// </summary>
        private static (bool Timed, Regex Pattern) Compile(string pattern)
        {
            var timed = pattern.EndsWith('T');
            var body = timed ? pattern[..^1] : pattern;
            var sb = new StringBuilder("^");

            for (var index = 0; index < body.Length; index++)
            {
                switch (body[index])
                {
                    case 'x':
                        sb.Append("\\d");
                        break;
                    case '.':
                        sb.Append('*');
                        break;
                    case '[':
                        var close = body.IndexOf(']', index);
                        sb.Append(body[index..(close + 1)]);
                        index = close;
                        break;
                    default:
                        sb.Append(Regex.Escape(body[index].ToString()));
                        break;
                }
            }

            return (timed, new Regex(sb.Append('$').ToString()));
        }

        /// <summary>Whether any pattern matches these digits without waiting for more.</summary>
        private static bool DialsAtOnce(string map, string dialled) =>
            map.Split('|').Select(Compile).Any(p => !p.Timed && p.Pattern.IsMatch(dialled));

        /// <summary>
        /// Whether the phone would send part of this number before the user had finished typing it:
        /// true if any prefix short of the whole thing matches a pattern that does not wait.
        /// </summary>
        private static bool SendsEarly(string map, string dialled) =>
            Enumerable.Range(1, dialled.Length - 1).Any(length => DialsAtOnce(map, dialled[..length]));

        [Fact]
        public void The_map_for_the_labs_extensions_matches_the_expected_file()
        {
            Assert.Equal(Expected("polycom-digitmap-lab.txt"), PolycomDigitMap.For(LabExtensions()));
        }

        /// <summary>
        /// The whole point: a ten- or eleven-digit number typed into a transfer goes out whole.
        /// The numbers here start 5 and 1-8 rather than 2-9-1-1, because the NANP reserves N11 as
        /// service codes — there is no 211 area code, which is what makes <c>[2-9]11</c> safe to
        /// dial the instant it matches.
        /// </summary>
        [Theory]
        [InlineData("5551234567")]
        [InlineData("15551234567")]
        [InlineData("18005551212")]
        public void A_long_number_is_never_sent_before_it_is_finished(string number)
        {
            var map = PolycomDigitMap.For(LabExtensions());

            Assert.False(SendsEarly(map, number), $"The map sent part of {number} early: {map}");

            // And having typed all of it, the user does not have to press anything.
            Assert.True(DialsAtOnce(map, number));
        }

        /// <summary>The map that shipped did cut a ten-digit number short, which is why this exists.</summary>
        [Fact]
        public void The_map_this_replaced_sent_four_digits_and_stopped()
        {
            Assert.True(SendsEarly(OldMap, "5551234567"));
        }

        /// <summary>
        /// An extension waits: three digits could be the start of anything, so the phone sends it
        /// after the timeout — or at once when the user presses #, which Polycom takes as "send".
        /// </summary>
        [Fact]
        public void An_extension_waits_rather_than_going_out_at_once()
        {
            var map = PolycomDigitMap.For(LabExtensions());

            Assert.False(DialsAtOnce(map, "100"));
            Assert.Contains("1xxT", map);
        }

        /// <summary>Emergency and the other service codes go the moment they are dialled.</summary>
        [Theory]
        [InlineData("911")]
        [InlineData("411")]
        public void A_service_code_goes_at_once(string number)
        {
            Assert.True(DialsAtOnce(PolycomDigitMap.For(LabExtensions()), number));
        }

        /// <summary>
        /// Seven-digit local dialing is the first seven digits of a ten-digit number, so it waits.
        /// Without that, a ten-digit number would be cut to seven exactly as it used to be cut to
        /// four.
        /// </summary>
        [Fact]
        public void Seven_digit_local_dialing_waits()
        {
            var map = PolycomDigitMap.For(LabExtensions());

            Assert.False(DialsAtOnce(map, "5551234"));
            Assert.Contains("[2-9]xxxxxxT", map);
        }

        /// <summary>
        /// A feature code can be any length — <c>*8</c> takes the ringing extension after it — so
        /// it waits, and the star keeps it out of the way of everything else.
        /// </summary>
        [Fact]
        public void A_feature_code_waits()
        {
            var map = PolycomDigitMap.For(LabExtensions());

            Assert.False(DialsAtOnce(map, "*97"));
            Assert.False(DialsAtOnce(map, "*8100"));
        }

        /// <summary>
        /// One pattern per length of extension, with the leading digit narrowed to what the site
        /// really uses: a site on 100–104 and 2000–2003 dials both without waiting on a wildcard
        /// that would also match somebody's mobile.
        /// </summary>
        [Fact]
        public void There_is_one_pattern_per_length_of_extension()
        {
            var extensions = LabExtensions();
            extensions.Add(new Extension { Number = "2000", Name = "Warehouse", Secret = "AAAAbbbbCCCCdddd1111" });
            extensions.Add(new Extension { Number = "3000", Name = "Yard", Secret = "AAAAbbbbCCCCdddd1111" });

            Assert.StartsWith("1xxT|[23]xxxT|", PolycomDigitMap.For(extensions));
        }

        /// <summary>
        /// A system with no extensions yet still gets a usable map: the outside world, the service
        /// codes and the feature codes. Nothing in it can truncate a number either.
        /// </summary>
        [Fact]
        public void A_system_with_no_extensions_still_gets_the_rest_of_the_map()
        {
            var map = PolycomDigitMap.For(new List<Extension>());

            Assert.Equal("*xx.T|[2-9]11|[2-9]xxxxxxT|[2-9]xxxxxxxxx|1xxxxxxxxxx|0T", map);
            Assert.False(SendsEarly(map, "15551234567"));
        }

        /// <summary>
        /// The map is a pure function of the numbers, so an extension whose number is not one we
        /// would ever write cannot put a pattern in it.
        /// </summary>
        [Fact]
        public void A_number_that_is_not_an_extension_number_is_left_out()
        {
            var extensions = new List<Extension>
            {
                new() { Number = "1", Name = "Too short", Secret = "AAAAbbbbCCCCdddd1111" },
                new() { Number = "1234567", Name = "Too long", Secret = "AAAAbbbbCCCCdddd1111" },
            };

            Assert.Equal(PolycomDigitMap.For(new List<Extension>()), PolycomDigitMap.For(extensions));
        }
    }
}
