using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// What CertificateRepository.Current picks (D99/D101): the newest usable certificate row
    /// and nothing else — an expired or unissued row never reaches Kestrel's bindings or the
    /// pjsip TLS transport.
    /// </summary>
    public class CertificateRepositoryTests : IDisposable
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-22T12:00:00Z");

        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-certs-").FullName;
        private readonly Database database;
        private readonly CertificateRepository certificates;

        public CertificateRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.certificates = new CertificateRepository(this.database);
        }

        private Certificate Add(string name, int daysLeft)
        {
            var certificate = new Certificate
            {
                Name = name,
                Hostnames = "pbx.example.com",
                CertificatePem = "-----BEGIN CERTIFICATE-----",
                KeyPem = "-----BEGIN PRIVATE KEY-----",
                ExpiresUtc = Now.AddDays(daysLeft).ToString("u"),
            };
            certificate.CertificateID = this.certificates.Insert(certificate);
            return certificate;
        }

        [Fact]
        public void Current_returns_the_newest_usable_certificate()
        {
            var older = this.Add("older", 40);
            var newer = this.Add("newer", 80);

            var current = this.certificates.Current(Now);

            Assert.Equal(newer.CertificateID, current?.CertificateID);
        }

        [Fact]
        public void Current_returns_null_when_every_certificate_is_expired_or_unissued()
        {
            this.Add("expired", -1);
            this.certificates.Insert(new Certificate { Name = "unissued", Hostnames = "pbx.example.com" });

            Assert.Null(this.certificates.Current(Now));
        }

        [Fact]
        public void Current_skips_a_disabled_certificate()
        {
            var disabled = this.Add("disabled", 60);
            disabled.Enabled = false;
            this.certificates.Update(disabled);

            Assert.Null(this.certificates.Current(Now));
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }
    }
}
