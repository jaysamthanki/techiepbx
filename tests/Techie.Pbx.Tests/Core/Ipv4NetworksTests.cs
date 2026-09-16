using System.Net;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The CIDR matching behind the local sign-in bypass (D24). Getting this wrong either locks an
    /// admin out or lets the wrong network in, so it is worth being exact about.
    /// </summary>
    public class Ipv4NetworksTests
    {
        private static Ipv4Networks Networks(params string[] cidrs) => new(cidrs);

        [Theory]
        [InlineData("10.8.20.1")]
        [InlineData("10.8.20.255")]
        [InlineData("10.8.20.0")]
        public void An_address_inside_the_range_matches(string address)
        {
            Assert.True(Networks("10.8.20.0/24").Contains(IPAddress.Parse(address)));
        }

        [Theory]
        [InlineData("10.8.21.1")]
        [InlineData("10.9.20.1")]
        [InlineData("203.0.113.10")]
        public void An_address_outside_the_range_does_not(string address)
        {
            Assert.False(Networks("10.8.20.0/24").Contains(IPAddress.Parse(address)));
        }

        [Fact]
        public void A_bare_address_is_that_address_and_nothing_else()
        {
            var networks = Networks("127.0.0.1");

            Assert.True(networks.Contains(IPAddress.Parse("127.0.0.1")));
            Assert.False(networks.Contains(IPAddress.Parse("127.0.0.2")));
            Assert.Equal("127.0.0.1/32", networks.ToString());
        }

        [Fact]
        public void A_thirty_two_is_the_same_as_a_bare_address()
        {
            var networks = Networks("10.8.20.5/32");

            Assert.True(networks.Contains(IPAddress.Parse("10.8.20.5")));
            Assert.False(networks.Contains(IPAddress.Parse("10.8.20.6")));
        }

        /// <summary>
        /// Someone writing the address of the machine they are sitting at, with the prefix of the
        /// network it is on, means the network. Rejecting that would be pedantry.
        /// </summary>
        [Fact]
        public void Host_bits_are_ignored_rather_than_refused()
        {
            var networks = Networks("10.8.20.5/24");

            Assert.Equal("10.8.20.0/24", networks.ToString());
            Assert.True(networks.Contains(IPAddress.Parse("10.8.20.200")));
        }

        /// <summary>Kestrel hands us ::ffff:10.8.20.5 when it is listening on a dual stack socket.</summary>
        [Fact]
        public void An_ipv4_address_mapped_into_ipv6_still_matches()
        {
            var networks = Networks("10.8.20.0/24");

            Assert.True(networks.Contains(IPAddress.Parse("10.8.20.5").MapToIPv6()));
            Assert.False(networks.Contains(IPAddress.Parse("10.8.21.5").MapToIPv6()));
        }

        [Fact]
        public void A_real_ipv6_address_never_matches()
        {
            Assert.False(Networks("0.0.0.0/0").Contains(IPAddress.Parse("2001:db8::1")));
        }

        [Fact]
        public void No_networks_matches_nothing_and_a_null_address_matches_nothing()
        {
            Assert.False(Networks().Contains(IPAddress.Parse("10.8.20.5")));
            Assert.False(Networks("10.8.20.0/24").Contains(null));
        }

        [Fact]
        public void Several_ranges_are_all_considered()
        {
            var networks = Networks("127.0.0.1/32", "10.8.20.0/24");

            Assert.True(networks.Contains(IPAddress.Parse("127.0.0.1")));
            Assert.True(networks.Contains(IPAddress.Parse("10.8.20.9")));
            Assert.False(networks.Contains(IPAddress.Parse("192.168.1.1")));
        }

        [Fact]
        public void Blank_entries_are_skipped_and_whitespace_is_forgiven()
        {
            var networks = Networks("", "  10.8.20.0/24  ", "   ");

            Assert.Single(networks.Networks);
            Assert.True(networks.Contains(IPAddress.Parse("10.8.20.7")));
        }

        [Theory]
        [InlineData("not-an-address")]
        [InlineData("10.8.20.0/33")]
        [InlineData("10.8.20.0/-1")]
        [InlineData("10.8.20.0/")]
        [InlineData("10.8.20.0/24/8")]
        [InlineData("2001:db8::/32")]
        public void Anything_that_will_not_parse_is_a_validation_error(string entry)
        {
            var ex = Assert.Throws<ValidationFailedException>(() => Networks(entry));

            Assert.Contains(entry.Trim(), ex.Message);
        }

        [Fact]
        public void Every_bad_entry_is_reported_not_just_the_first()
        {
            var ex = Assert.Throws<ValidationFailedException>(() => Networks("nonsense", "10.8.20.0/24", "also-nonsense"));

            Assert.Equal(2, ex.Errors.Count);
        }
    }
}
