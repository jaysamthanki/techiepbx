using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Techie.Pbx.Web.Security;

namespace Techie.Pbx.Tests.Web
{
    /// <summary>
    /// Who is an admin (D165): a signed-in user carrying the Entra app role, under either
    /// spelling of the claim — and the local bypass, which has to be one.
    /// </summary>
    public class AdminRoleTests
    {
        private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
            new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "Test"));

        [Fact]
        public void Nobody_is_not_an_admin()
        {
            Assert.False(AdminRole.IsAdmin(null));
        }

        [Fact]
        public void An_unauthenticated_identity_with_the_role_is_not_an_admin()
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(AdminRole.ClaimType, AdminRole.Value) }));

            Assert.False(AdminRole.IsAdmin(user));
        }

        [Fact]
        public void A_signed_in_user_without_the_role_is_not_an_admin()
        {
            Assert.False(AdminRole.IsAdmin(User(("preferred_username", "sam@example.com"))));
        }

        [Fact]
        public void Another_role_is_not_the_admin_role()
        {
            Assert.False(AdminRole.IsAdmin(User((AdminRole.ClaimType, "TNPBX User"))));
        }

        [Theory]
        [InlineData(AdminRole.ClaimType, "TNPBX.Admin")]
        [InlineData(AdminRole.ClaimType, "tnpbx.admin")]
        [InlineData(AdminRole.MappedClaimType, "TNPBX.Admin")]
        [InlineData(AdminRole.MappedClaimType, "TNPBX.ADMIN")]
        public void The_role_counts_under_either_spelling_and_any_case(string type, string value)
        {
            Assert.True(AdminRole.IsAdmin(User((type, value))));
        }

        [Fact]
        public void The_role_is_found_among_several()
        {
            Assert.True(AdminRole.IsAdmin(User((AdminRole.ClaimType, "Other"), (AdminRole.ClaimType, AdminRole.Value))));
        }

        [Fact]
        public async Task The_local_bypass_is_an_admin()
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");

            var middleware = new LocalBypassMiddleware(
                _ => Task.CompletedTask,
                new LocalBypassSettings(true, new[] { "10.0.0.0/24" }));

            await middleware.InvokeAsync(context);

            Assert.True(AdminRole.IsAdmin(context.User));
        }
    }
}
