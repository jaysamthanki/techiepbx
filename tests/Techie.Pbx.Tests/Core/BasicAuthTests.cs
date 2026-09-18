using System.Text;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The gate on the provisioning endpoint (D77). The part worth pinning down is what it refuses:
    /// a scheme that is not Basic, base64 that is not base64, and — the one that matters — a
    /// username or password that has not been configured at all.
    /// </summary>
    public class BasicAuthTests
    {
        private static string Header(string username, string password) =>
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

        [Fact]
        public void A_header_is_split_into_username_and_password()
        {
            Assert.True(BasicAuth.TryParse(Header("phones", "s3cret-pass"), out var username, out var password));
            Assert.Equal("phones", username);
            Assert.Equal("s3cret-pass", password);
        }

        /// <summary>A username cannot contain a colon, so the first one splits the pair.</summary>
        [Fact]
        public void A_password_may_contain_a_colon()
        {
            Assert.True(BasicAuth.TryParse(Header("phones", "a:b:c"), out var username, out var password));
            Assert.Equal("phones", username);
            Assert.Equal("a:b:c", password);
        }

        [Fact]
        public void The_scheme_name_is_matched_without_regard_to_case()
        {
            Assert.True(BasicAuth.TryParse("basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("a:b")), out _, out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Basic")]
        [InlineData("Bearer abcdef")]
        [InlineData("Basic !!!not base64!!!")]
        public void Anything_that_is_not_a_basic_header_is_refused(string? header)
        {
            Assert.False(BasicAuth.TryParse(header, out _, out _));
        }

        /// <summary>Base64 that decodes but has no colon is not a credential.</summary>
        [Fact]
        public void A_pair_with_no_colon_is_refused()
        {
            Assert.False(BasicAuth.TryParse("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("nocolon")), out _, out _));
        }

        [Fact]
        public void The_right_credentials_match()
        {
            Assert.True(BasicAuth.Matches(Header("phones", "s3cret-pass"), "phones", "s3cret-pass"));
        }

        [Theory]
        [InlineData("phones", "wrong")]
        [InlineData("wrong", "s3cret-pass")]
        [InlineData("phones", "s3cret-pas")]
        [InlineData("phones", "s3cret-passs")]
        [InlineData("Phones", "s3cret-pass")]
        public void The_wrong_ones_do_not(string username, string password)
        {
            Assert.False(BasicAuth.Matches(Header(username, password), "phones", "s3cret-pass"));
        }

        /// <summary>
        /// The important one: provisioning with nothing configured is off, not open. Blank expected
        /// credentials match nothing at all, including a blank header (D77).
        /// </summary>
        [Theory]
        [InlineData("", "")]
        [InlineData("phones", "")]
        [InlineData("", "s3cret-pass")]
        public void Unconfigured_credentials_match_nothing(string expectedUsername, string expectedPassword)
        {
            Assert.False(BasicAuth.Matches(Header(expectedUsername, expectedPassword), expectedUsername, expectedPassword));
            Assert.False(BasicAuth.Matches(Header("phones", "s3cret-pass"), expectedUsername, expectedPassword));
            Assert.False(BasicAuth.Matches(null, expectedUsername, expectedPassword));
        }
    }
}
