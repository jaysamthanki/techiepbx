using System.Security.Claims;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Security;

namespace Techie.Pbx.Tests.Web
{
    /// <summary>
    /// Which extensions a sign-in may open in the web client (D164, D165, D166). This is the rule that
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
            Web("106", "reception@example.com", "Sam@Example.com"),
            Web("107", "sam@example.com", "alex@example.com"),
        };

        private static string[] Numbers(ClaimsPrincipal? user) =>
            PhoneUser.Extensions(user, All).Select(e => e.Number).ToArray();

        private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
            new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "Test"));

        private static Extension Web(string number, string voicemailEmail, string userEmail = "") =>
            new() { Number = number, Name = "Extension " + number, Enabled = true, WebClient = true, UserEmail = userEmail, VoicemailEmail = voicemailEmail };

        private static Extension Voicemail(Extension extension)
        {
            extension.VoicemailEnabled = true;
            return extension;
        }

        /// <summary>
        /// The Voicemail badge's mailboxes (D169): owned, enabled, web-enabled and with voicemail
        /// on — and only owned, even for an admin.
        /// </summary>
        [Fact]
        public void The_badge_counts_only_the_users_own_voicemail_mailboxes()
        {
            var extensions = new List<Extension>
            {
                Voicemail(Web("100", "sam@example.com")),
                Web("101", "sam@example.com"),
                Voicemail(Web("102", "alex@example.com")),
                Voicemail(new Extension { Number = "104", Enabled = true, WebClient = false, VoicemailEmail = "sam@example.com" }),
                Voicemail(new Extension { Number = "105", Enabled = false, WebClient = true, VoicemailEmail = "sam@example.com" }),
                Voicemail(Web("106", "reception@example.com", "sam@example.com")),
            };

            Assert.Equal(new[] { "100", "106" }, PhoneUser.Mailboxes(User((PhoneUser.EmailClaim, "Sam@Example.com")), extensions));
            Assert.Equal(new[] { "102" }, PhoneUser.Mailboxes(User((AdminRole.ClaimType, AdminRole.Value), (PhoneUser.EmailClaim, "alex@example.com")), extensions));
            Assert.Empty(PhoneUser.Mailboxes(User((AdminRole.ClaimType, AdminRole.Value)), extensions));
            Assert.Empty(PhoneUser.Mailboxes(new ClaimsPrincipal(new ClaimsIdentity()), extensions));
            Assert.Empty(PhoneUser.Mailboxes(null, extensions));
        }

        [Fact]
        public void An_admin_gets_every_web_enabled_extension()
        {
            var admin = User((AdminRole.ClaimType, AdminRole.Value), (PhoneUser.EmailClaim, "nobody@example.com"));

            Assert.Equal(new[] { "100", "101", "102", "103", "106", "107" }, Numbers(admin));
        }

        [Fact]
        public void A_phone_user_gets_only_their_own_whatever_the_case()
        {
            // 100 and 101 by the voicemail email fallback, 106 by its user email — and not 107,
            // whose voicemail goes to sam but which belongs to alex.
            Assert.Equal(new[] { "100", "101", "106" }, Numbers(User((PhoneUser.EmailClaim, " SAM@example.com "))));
        }

        [Fact]
        public void The_user_email_and_the_fallback_both_count_for_the_same_user()
        {
            Assert.Equal(new[] { "102", "107" }, Numbers(User((PhoneUser.UpnClaim, "alex@example.com"))));
        }

        [Fact]
        public void A_voicemail_email_behind_a_user_email_owns_nothing()
        {
            Assert.Empty(Numbers(User((PhoneUser.EmailClaim, "reception@example.com"))));
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

        [Theory]
        // The user email matches, whatever the voicemail email is.
        [InlineData("sam@example.com", "sam@example.com", "", true)]
        [InlineData("SAM@EXAMPLE.COM", " sam@example.com ", "reception@example.com", true)]
        // No user email: the voicemail email is the fallback.
        [InlineData("sam@example.com", "", "sam@example.com", true)]
        [InlineData("sam@example.com", "  ", "Sam@Example.com", true)]
        // A user email that is somebody else's is the owner, and there is no fallback past it.
        [InlineData("sam@example.com", "alex@example.com", "sam@example.com", false)]
        [InlineData("sam@example.com", "alex@example.com", "", false)]
        // Neither: nobody.
        [InlineData("sam@example.com", "", "", false)]
        [InlineData("", "", "", false)]
        [InlineData(null, "", "", false)]
        public void The_user_email_owns_and_the_voicemail_email_is_only_the_fallback(
            string? email, string userEmail, string voicemailEmail, bool owns)
        {
            Assert.Equal(owns, PhoneUser.Owns(email, Web("100", voicemailEmail, userEmail)));
        }
    }
}
