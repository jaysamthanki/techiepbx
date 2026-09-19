using Dapper;
using log4net;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// The certificate rows (D97). Two things read them — Kestrel at startup and the pjsip TLS
    /// transport at apply time — so a write here is a config change like any other and raises the
    /// apply marker (D26): ordering or renewing a certificate changes what <c>pjsip.conf</c> and
    /// <c>tnpbx-cert.pem</c> should say, and the red apply button is how an admin is told.
    ///
    /// Nothing in this class logs a PEM. The certificate half is public information, but the key
    /// is in the same row, and a repository that logged one would eventually log the other.
    /// </summary>
    public class CertificateRepository
    {
        private const string Columns =
            "CertificateID, Name, Hostnames, CertificatePem, ChainPem, KeyPem, ExpiresUtc, LastError, Enabled";

        private const int SqliteConstraintError = 19;

        private static readonly ILog Log = LogManager.GetLogger(typeof(CertificateRepository));

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public CertificateRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        /// <summary>
        /// The certificate the server should actually be using: the usable one that expires
        /// furthest away. One row feeds both Kestrel and the SIP transport, so when a site has
        /// several, "the one with the most life left" is the least surprising choice.
        /// </summary>
        public Certificate? Current(DateTimeOffset now) =>
            this.GetAll()
                .Where(certificate => certificate.IsUsable(now))
                .OrderByDescending(certificate => certificate.Expires)
                .FirstOrDefault();

        public void Delete(long certificateID)
        {
            using var connection = this.database.Open();
            connection.Execute("DELETE FROM Certificates WHERE CertificateID = @certificateID", new { certificateID });

            this.pending.Raise();
        }

        public List<Certificate> GetAll()
        {
            using var connection = this.database.Open();
            return connection.Query<Certificate>($"SELECT {Columns} FROM Certificates ORDER BY Name").ToList();
        }

        public Certificate? GetByID(long certificateID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<Certificate>(
                $"SELECT {Columns} FROM Certificates WHERE CertificateID = @certificateID", new { certificateID });
        }

        public long Insert(Certificate certificate)
        {
            this.ThrowIfInvalid(certificate);

            using var connection = this.database.Open();
            try
            {
                certificate.CertificateID = connection.ExecuteScalar<long>(
                    "INSERT INTO Certificates (Name, Hostnames, CertificatePem, ChainPem, KeyPem, ExpiresUtc, LastError, Enabled) " +
                    "VALUES (@Name, @Hostnames, @CertificatePem, @ChainPem, @KeyPem, @ExpiresUtc, @LastError, @Enabled); " +
                    "SELECT last_insert_rowid();",
                    certificate);

                this.pending.Raise();
                Log.Info($"Certificate '{certificate.Name}' created for {certificate.Hostnames}");

                return certificate.CertificateID;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A certificate called '{certificate.Name}' already exists.");
            }
        }

        public void Update(Certificate certificate)
        {
            this.ThrowIfInvalid(certificate);

            using var connection = this.database.Open();
            try
            {
                var rows = connection.Execute(
                    "UPDATE Certificates SET Name = @Name, Hostnames = @Hostnames, CertificatePem = @CertificatePem, " +
                    "ChainPem = @ChainPem, KeyPem = @KeyPem, ExpiresUtc = @ExpiresUtc, LastError = @LastError, " +
                    "Enabled = @Enabled WHERE CertificateID = @CertificateID",
                    certificate);
                if (rows == 0)
                    throw new ValidationFailedException($"CertificateID {certificate.CertificateID} does not exist.");

                this.pending.Raise();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A certificate called '{certificate.Name}' already exists.");
            }
        }

        /// <summary>
        /// The model's own rules. Nothing here needs the database: a certificate points at no other
        /// table, which is the whole reason this feature adds one table and no foreign keys.
        /// </summary>
        private void ThrowIfInvalid(Certificate certificate)
        {
            certificate.Hostnames = Certificate.NormalizeHostnames(certificate.Hostnames);
            certificate.Name = certificate.Name.Trim();

            var errors = certificate.Validate();

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
