using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The certificate model's own rules: what counts as usable (the same question Kestrel's
    /// bindings and the TLS transport both ask, D99/D101), when renewal is due (D100), and the
    /// hostname handling an order is started from.
    /// </summary>
    public class CertificateTests
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-22T12:00:00Z");

        private static Certificate Issued(int daysLeft) => new()
        {
            Name = "pbx",
            Hostnames = "pbx.example.com",
            CertificatePem = "-----BEGIN CERTIFICATE-----",
            KeyPem = "-----BEGIN PRIVATE KEY-----",
            ExpiresUtc = Now.AddDays(daysLeft).ToString("u"),
        };

        [Fact]
        public void An_issued_certificate_that_has_not_expired_is_usable()
        {
            Assert.True(Issued(60).IsUsable(Now));
        }

        [Fact]
        public void An_expired_certificate_is_not_usable()
        {
            Assert.False(Issued(-1).IsUsable(Now));
        }

        [Fact]
        public void A_disabled_or_unissued_certificate_is_not_usable()
        {
            var disabled = Issued(60);
            disabled.Enabled = false;
            Assert.False(disabled.IsUsable(Now));

            var unissued = new Certificate { Name = "pbx", Hostnames = "pbx.example.com" };
            Assert.False(unissued.IsUsable(Now));
        }

        [Fact]
        public void Renewal_is_due_inside_the_threshold_and_for_rows_that_never_issued()
        {
            Assert.False(Issued(60).NeedsRenewal(Now));
            Assert.True(Issued(Certificate.RenewalThresholdDays - 1).NeedsRenewal(Now));
            Assert.True(Issued(Certificate.RenewalThresholdDays).NeedsRenewal(Now));
            Assert.True(new Certificate { Name = "pbx", Hostnames = "pbx.example.com" }.NeedsRenewal(Now));
        }

        [Fact]
        public void Hostnames_split_on_commas_spaces_and_lines()
        {
            Assert.Equal(
                new[] { "pbx.example.com", "sip.example.com" },
                Certificate.ParseHostnames("PBX.Example.com, sip.example.com\n").ToArray());
        }

        [Fact]
        public void Validation_rejects_wildcards_because_http01_cannot_prove_them()
        {
            var certificate = new Certificate { Name = "pbx", Hostnames = "*.example.com" };

            Assert.Contains(certificate.Validate(), error => error.Contains("DNS challenge"));
        }

        [Fact]
        public void Validation_rejects_duplicate_hostnames()
        {
            var certificate = new Certificate { Name = "pbx", Hostnames = "pbx.example.com,pbx.example.com" };

            Assert.Contains(certificate.Validate(), error => error.Contains("only be named once"));
        }

        [Fact]
        public void CombinedPem_puts_certificate_chain_then_key_each_on_their_own_lines()
        {
            var combined = CertificatePem.Combine(
                "-----BEGIN CERTIFICATE-----\nAAA\n-----END CERTIFICATE-----",
                "-----BEGIN CERTIFICATE-----\nBBB\n-----END CERTIFICATE-----",
                "-----BEGIN PRIVATE KEY-----\nCCC\n-----END PRIVATE KEY-----");

            Assert.Equal(
                "-----BEGIN CERTIFICATE-----\nAAA\n-----END CERTIFICATE-----\n" +
                "-----BEGIN CERTIFICATE-----\nBBB\n-----END CERTIFICATE-----\n" +
                "-----BEGIN PRIVATE KEY-----\nCCC\n-----END PRIVATE KEY-----\n",
                combined);
        }

        [Fact]
        public void An_empty_part_of_the_combined_pem_is_simply_skipped()
        {
            Assert.Equal(
                "-----BEGIN CERTIFICATE-----\nAAA\n-----END CERTIFICATE-----\n",
                CertificatePem.Combine("-----BEGIN CERTIFICATE-----\nAAA\n-----END CERTIFICATE-----", "", null));
        }
    }
}
