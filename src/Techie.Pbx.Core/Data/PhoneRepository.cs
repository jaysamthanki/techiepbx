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
        private const string Columns = "PhoneID, Mac, Name, Model, Firmware, LastIP, LastConfig, ExtensionID, Enabled";

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
            this.ThrowIfInvalid(phone);

            using var connection = this.database.Open();
            try
            {
                phone.PhoneID = connection.ExecuteScalar<long>(
                    "INSERT INTO Phones (Mac, Name, Model, Firmware, LastIP, LastConfig, ExtensionID, Enabled) " +
                    "VALUES (@Mac, @Name, @Model, @Firmware, @LastIP, @LastConfig, @ExtensionID, @Enabled); " +
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
        /// the enabled flag — is left exactly as it was.
        ///
        /// The caller has already decided this request may be served at all; this only records it.
        /// </summary>
        public Phone Register(string mac, string model, string firmware, string address)
        {
            var phone = this.GetByMac(mac);
            var isNew = phone == null;

            phone ??= new Phone { Mac = mac };

            phone.Firmware = firmware;
            phone.LastConfig = Timestamp();
            phone.LastIP = address;
            phone.Model = model;

            if (isNew)
            {
                this.Insert(phone);
                Log.Info($"Phone {mac} ({model}, firmware {firmware}) auto-added from {address}");
            }
            else
            {
                this.Update(phone);
            }

            return phone;
        }

        public void Update(Phone phone)
        {
            this.ThrowIfInvalid(phone);

            using var connection = this.database.Open();
            try
            {
                var rows = connection.Execute(
                    "UPDATE Phones SET Mac = @Mac, Name = @Name, Model = @Model, Firmware = @Firmware, " +
                    "LastIP = @LastIP, LastConfig = @LastConfig, ExtensionID = @ExtensionID, Enabled = @Enabled " +
                    "WHERE PhoneID = @PhoneID",
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
        /// The model's own rules, plus the one that needs the database: a phone can only be linked
        /// to an extension that exists. The foreign key would catch that as well, but a message an
        /// admin can act on is better than a constraint violation.
        /// </summary>
        private void ThrowIfInvalid(Phone phone)
        {
            var errors = phone.Validate();

            if (phone.ExtensionID is > 0 &&
                new ExtensionRepository(this.database).GetByID(phone.ExtensionID.Value) == null)
            {
                errors.Add("That extension is not there any more. Choose another.");
            }

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
