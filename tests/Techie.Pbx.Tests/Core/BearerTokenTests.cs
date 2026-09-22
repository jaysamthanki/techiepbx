using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The gate on the voicemail notify endpoint (D129). It is the only thing protecting that
    /// endpoint, so the cases worth naming are the ones where a mistake would open it: a blank
    /// configured token, a header that is nearly right, and another scheme's credentials.
    /// </summary>
    public class BearerTokenTests
    {
        [Fact]
        public void The_right_token_matches()
        {
            Assert.True(BearerToken.Matches("Bearer s3cret-token", "s3cret-token"));
        }

        /// <summary>The scheme name is a word in a header, so its case is not the credential.</summary>
        [Fact]
        public void The_scheme_is_matched_whatever_case_it_is_written_in()
        {
            Assert.True(BearerToken.Matches("bearer s3cret-token", "s3cret-token"));
            Assert.True(BearerToken.Matches("BEARER s3cret-token", "s3cret-token"));
        }

        /// <summary>
        /// A system with no token configured has a closed endpoint, not an open one. Nothing may
        /// match a blank expected token — least of all a blank header.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Bearer ")]
        [InlineData("Bearer anything")]
        public void Nothing_matches_when_no_token_is_configured(string? header)
        {
            Assert.False(BearerToken.Matches(header, ""));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("s3cret-token")]
        [InlineData("Basic czNjcmV0LXRva2Vu")]
        [InlineData("Bearer")]
        [InlineData("Bearer ")]
        [InlineData("Bearer s3cret-toke")]
        [InlineData("Bearer s3cret-token ")]
        [InlineData("Bearer S3CRET-TOKEN")]
        public void Anything_else_does_not_match(string? header)
        {
            // The trailing-space case is the one that looks wrong and is not: the header is
            // trimmed, so "Bearer s3cret-token " is the same credential. It is listed here to be
            // explicit about which of these is which.
            var expected = header == "Bearer s3cret-token ";

            Assert.Equal(expected, BearerToken.Matches(header, "s3cret-token"));
        }

        [Fact]
        public void A_header_long_enough_to_be_an_attack_is_not_parsed()
        {
            Assert.False(BearerToken.TryParse("Bearer " + new string('x', 2000), out _));
        }

        [Fact]
        public void The_token_is_what_follows_the_scheme()
        {
            Assert.True(BearerToken.TryParse("Bearer  s3cret-token ", out var token));
            Assert.Equal("s3cret-token", token);
        }
    }
}
