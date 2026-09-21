using System.Globalization;
using Dapper;
using log4net;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    /// <summary>
    /// The provisioned phones. Unlike every other repository here, this one raises no
    /// config-pending marker: nothing in this table is rendered into /etc/asterisk, because a
    /// phone's config is generated per request and never stored (D79). Saving a phone therefore
    /// changes what the next provisioning fetch returns and nothing else.
    ///
    /// <see cref="Register"/> is the write the phone itself causes, and it is the only one that can
    /// create a row without an admin (D78).
    /// </summary>
    public class PhoneRepository
    {
        private const string Columns = "PhoneID, Mac, Name, Model, Firmware, LastIP, LastConfig, Enabled, Brand";

        private const int SqliteConstraintError = 19;

        private static readonly ILog Log = LogManager.GetLogger(typeof(PhoneRepository));

        private readonly Database database;

        public PhoneRepository(Database database)
        {
            this.database = database;
        }

        public void Delete(long phoneID)
        {
            using var connection = this.database.Open();
            connection.Execute("DELETE FROM Phones WHERE PhoneID = @phoneID", new { phoneID });
        }

        public List<Phone> GetAll()
        {
            using var connection = this.database.Open();
            return connection.Query<Phone>($"SELECT {Columns} FROM Phones ORDER BY Mac").ToList();
        }

        public Phone? GetByID(long phoneID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<Phone>(
                $"SELECT {Columns} FROM Phones WHERE PhoneID = @phoneID", new { phoneID });
        }

        /// <summary>
        /// The lookup the provisioning endpoint makes on every request. The MAC has already been
        /// matched against its own pattern by then, and it is still a Dapper parameter.
        /// </summary>
        public Phone? GetByMac(string mac)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<Phone>(
                $"SELECT {Columns} FROM Phones WHERE Mac = @mac", new { mac });
        }

        public long Insert(Phone phone)
        {
            ThrowIfInvalid(phone);

            using var connection = this.database.Open();
            try
            {
                phone.PhoneID = connection.ExecuteScalar<long>(
                    "INSERT INTO Phones (Mac, Name, Model, Firmware, LastIP, LastConfig, Enabled, Brand) " +
                    "VALUES (@Mac, @Name, @Model, @Firmware, @LastIP, @LastConfig, @Enabled, @Brand); " +
                    "SELECT last_insert_rowid();",
                    phone);

                return phone.PhoneID;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A phone with MAC {phone.Mac} already exists.");
            }
        }

        /// <summary>
        /// What a phone fetching its config does to this table: an unknown MAC is added with what
        /// its User-Agent said about it, and a known one has its model, firmware, address and last
        /// contact brought up to date (D78). Everything an admin owns — the name, the extension,
        /// the enabled flag — is left exactly as it was. So is the brand: it is only ever set at
        /// insert time, never touched on an update, so a known phone's brand cannot silently
        /// change because a later request came in looking like the other vendor (D88).
        ///
        /// The caller has already decided this request may be served at all; this only records it.
        /// </summary>
        public Phone Register(string mac, string model, string firmware, string address, string brand = PhoneBrand.Polycom)
        {
            var phone = this.GetByMac(mac);
            var isNew = phone == null;

            phone ??= new Phone { Mac = mac, Brand = brand };

            phone.Firmware = firmware;
            phone.LastConfig = Timestamp();
            phone.LastIP = address;
            phone.Model = model;

            if (isNew)
            {
                this.Insert(phone);
                Log.Info($"Phone {mac} ({brand} {model}, firmware {firmware}) auto-added from {address}");
            }
            else
            {
                this.Update(phone);
            }

            return phone;
        }

        public void Update(Phone phone)
        {
            ThrowIfInvalid(phone);

            using var connection = this.database.Open();
            try
            {
                var rows = connection.Execute(
                    "UPDATE Phones SET Mac = @Mac, Name = @Name, Model = @Model, Firmware = @Firmware, " +
                    "LastIP = @LastIP, LastConfig = @LastConfig, Enabled = @Enabled, " +
                    "Brand = @Brand WHERE PhoneID = @PhoneID",
                    phone);
                if (rows == 0)
                    throw new ValidationFailedException($"PhoneID {phone.PhoneID} does not exist.");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"A phone with MAC {phone.Mac} already exists.");
            }
        }

        /// <summary>When something happened, as the table shows it: UTC, sortable as text.</summary>
        private static string Timestamp() =>
            DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture);

        /// <summary>
        /// The model's own rules, and only those: nothing on this row points at another table any
        /// more. Which extension a phone registers as is a key on it, and
        /// <see cref="PhoneButtonRepository"/> is what checks that the extension is still there
        /// (schema 020).
        /// </summary>
        private static void ThrowIfInvalid(Phone phone)
        {
            var errors = phone.Validate();

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
