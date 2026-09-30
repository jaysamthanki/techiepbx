using System.Security.Claims;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Security;

namespace Techie.Pbx.Tests.Web
{
    /// <summary>
    /// Which extensions a sign-in may open in the web client (D164, D165). This is the rule that
    /// decides who is handed a SIP secret, so every way of not owning an extension is pinned.
    /// </summary>
    public class PhoneUserTests
    {
        private static readonly List<Extension> All = new()
        {
            Web("100", "sam@example.com"),
            Web("101", "Sam@Example.com"),
            Web("102", "alex@example.com"),
            Web("103", ""),
            new Extension { Number = "104", Name = "Desk only", Enabled = true, WebClient = false, VoicemailEmail = "sam@example.com" },
            new Extension { Number = "105", Name = "Disabled", Enabled = false, WebClient = true, VoicemailEmail = "sam@example.com" },
        };

        private static string[] Numbers(ClaimsPrincipal? user) =>
            PhoneUser.Extensions(user, All).Select(e => e.Number).ToArray();

        private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
            new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "Test"));

        private static Extension Web(string number, string email) =>
            new() { Number = number, Name = "Extension " + number, Enabled = true, WebClient = true, VoicemailEmail = email };

        [Fact]
        public void An_admin_gets_every_web_enabled_extension()
        {
            var admin = User((AdminRole.ClaimType, AdminRole.Value), (PhoneUser.EmailClaim, "nobody@example.com"));

            Assert.Equal(new[] { "100", "101", "102", "103" }, Numbers(admin));
        }

        [Fact]
        public void A_phone_user_gets_only_their_own_whatever_the_case()
        {
            Assert.Equal(new[] { "100", "101" }, Numbers(User((PhoneUser.EmailClaim, " SAM@example.com "))));
        }

        [Fact]
        public void One_match_is_one_extension()
        {
            Assert.Equal(new[] { "102" }, Numbers(User((PhoneUser.UpnClaim, "alex@example.com"))));
        }

        [Fact]
        public void No_match_is_nothing()
        {
            Assert.Empty(Numbers(User((PhoneUser.EmailClaim, "pat@example.com"))));
        }

        [Fact]
        public void No_email_claim_does_not_match_an_extension_with_no_email()
        {
            Assert.Empty(Numbers(User(("name", "Pat"))));
        }

        [Fact]
        public void Nobody_signed_in_gets_nothing()
        {
            Assert.Empty(Numbers(null));
            Assert.Empty(Numbers(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(PhoneUser.EmailClaim, "sam@example.com") }))));
        }

        [Fact]
        public void The_email_claim_wins_over_the_upn()
        {
            var user = User((PhoneUser.UpnClaim, "sam@corp.example.com"), (PhoneUser.EmailClaim, "sam@example.com"));

            Assert.Equal("sam@example.com", PhoneUser.Email(user));
        }

        [Theory]
        [InlineData(PhoneUser.EmailClaim)]
        [InlineData(PhoneUser.MappedEmailClaim)]
        [InlineData(PhoneUser.UpnClaim)]
        [InlineData(PhoneUser.MappedUpnClaim)]
        public void The_address_is_read_from_either_claim_under_either_spelling(string type)
        {
            Assert.Equal("sam@example.com", PhoneUser.Email(User((type, "  sam@example.com  "))));
        }

        [Fact]
        public void An_empty_email_claim_falls_through_to_the_upn()
        {
            var user = User((PhoneUser.EmailClaim, "  "), (PhoneUser.UpnClaim, "sam@example.com"));

            Assert.Equal("sam@example.com", PhoneUser.Email(user));
        }

        [Fact]
        public void No_address_at_all_is_an_empty_string()
        {
            Assert.Equal("", PhoneUser.Email(User(("name", "Pat"))));
            Assert.Equal("", PhoneUser.Email(null));
        }

        [Theory]
        [InlineData("sam@example.com", "sam@example.com", true)]
        [InlineData("SAM@EXAMPLE.COM", " sam@example.com ", true)]
        [InlineData("sam@example.com", "samantha@example.com", false)]
        [InlineData("sam@example.com", "", false)]
        [InlineData("", "", false)]
        [InlineData(null, "", false)]
        public void Owning_is_the_same_address_and_never_an_empty_one(string? email, string voicemailEmail, bool owns)
        {
            Assert.Equal(owns, PhoneUser.Owns(email, Web("100", voicemailEmail)));
        }
    }
}
