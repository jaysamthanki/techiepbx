using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The phones table, and above all <see cref="PhoneRepository.Register"/> — the one write a
    /// phone causes for itself (D78). What it must do: add an unknown MAC, bring a known one up to
    /// date, and never touch the fields an admin owns.
    /// </summary>
    public class PhoneRepositoryTests : IDisposable
    {
        private const string Mac = "0004f2aabbcc";

        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-phones-").FullName;
        private readonly Database database;
        private readonly ExtensionRepository extensions;
        private readonly PhoneRepository phones;

        public PhoneRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.extensions = new ExtensionRepository(this.database);
            this.phones = new PhoneRepository(this.database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private long AddExtension(string number = "1001", bool enabled = true) =>
            this.extensions.Insert(new Extension
            {
                Number = number,
                Name = "Phone " + number,
                Secret = "AAAAbbbbCCCCdddd1111",
                Enabled = enabled,
            });

        [Fact]
        public void An_unknown_mac_is_auto_added_with_what_its_user_agent_said()
        {
            var phone = this.phones.Register(Mac, "VVX_410", "5.9.5.0614", "10.8.20.31");

            var loaded = this.phones.GetByMac(Mac)!;

            Assert.Equal(phone.PhoneID, loaded.PhoneID);
            Assert.Equal("VVX_410", loaded.Model);
            Assert.Equal("5.9.5.0614", loaded.Firmware);
            Assert.Equal("10.8.20.31", loaded.LastIP);
            Assert.NotEqual("", loaded.LastConfig);

            // An auto-added phone belongs to nobody until an admin says otherwise.
            Assert.Equal("", loaded.Name);
            Assert.Null(loaded.ExtensionID);
            Assert.True(loaded.Enabled);
        }

        [Fact]
        public void A_known_mac_is_updated_rather_than_added_again()
        {
            this.phones.Register(Mac, "VVX_410", "5.9.5.0614", "10.8.20.31");
            this.phones.Register(Mac, "VVX_410", "6.4.6.0123", "10.8.20.44");

            var all = this.phones.GetAll();

            Assert.Single(all);
            Assert.Equal("6.4.6.0123", all[0].Firmware);
            Assert.Equal("10.8.20.44", all[0].LastIP);
        }

        /// <summary>What an admin owns is not the phone's to overwrite.</summary>
        [Fact]
        public void Registering_again_keeps_the_name_extension_and_enabled_flag()
        {
            var extensionID = this.AddExtension();
            var phone = this.phones.Register(Mac, "VVX_410", "5.9.5.0614", "10.8.20.31");

            phone.Name = "Reception";
            phone.ExtensionID = extensionID;
            phone.Enabled = false;
            this.phones.Update(phone);

            this.phones.Register(Mac, "VVX_410", "6.4.6.0123", "10.8.20.44");

            var loaded = this.phones.GetByMac(Mac)!;

            Assert.Equal("Reception", loaded.Name);
            Assert.Equal(extensionID, loaded.ExtensionID);
            Assert.False(loaded.Enabled);
        }

        [Theory]
        [InlineData("0004F2AABBCC")]
        [InlineData("00:04:f2:aa:bb:cc")]
        [InlineData("0004f2aabbc")]
        [InlineData("0004f2aabbccdd")]
        [InlineData("0004f2aabbcg")]
        [InlineData("")]
        public void A_mac_that_is_not_twelve_lower_case_hex_digits_is_refused(string mac)
        {
            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.phones.Insert(new Phone { Mac = mac }));

            Assert.Contains("MAC address", ex.Message);
        }

        [Fact]
        public void Two_phones_cannot_share_a_mac()
        {
            this.phones.Insert(new Phone { Mac = Mac });

            var ex = Assert.Throws<ValidationFailedException>(() => this.phones.Insert(new Phone { Mac = Mac }));

            Assert.Contains("already exists", ex.Message);
        }

        [Fact]
        public void An_extension_that_does_not_exist_is_refused()
        {
            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.phones.Insert(new Phone { Mac = Mac, ExtensionID = 999 }));

            Assert.Contains("not there any more", ex.Message);
        }

        /// <summary>
        /// A model that has changed underneath us is the check the provisioning endpoint makes
        /// before it serves anything (D78). The rule lives on the model so it can be tested here.
        /// </summary>
        [Fact]
        public void A_phone_whose_model_changed_no_longer_matches()
        {
            var phone = this.phones.Register(Mac, "VVX_410", "5.9.5.0614", "10.8.20.31");

            Assert.True(phone.MatchesModel("VVX_410"));
            Assert.True(phone.MatchesModel("vvx_410"));
            Assert.False(phone.MatchesModel("VVX_501"));
            Assert.False(phone.MatchesModel(""));
        }

        /// <summary>A row created before the phone ever spoke has nothing to disagree with.</summary>
        [Fact]
        public void A_phone_with_no_model_recorded_matches_anything()
        {
            Assert.True(new Phone { Mac = Mac }.MatchesModel("VVX_410"));
        }

        [Fact]
        public void Update_and_delete()
        {
            var extensionID = this.AddExtension();
            var phone = this.phones.Register(Mac, "VVX_410", "5.9.5.0614", "10.8.20.31");

            phone.Name = "Reception";
            phone.ExtensionID = extensionID;
            this.phones.Update(phone);

            var loaded = this.phones.GetByID(phone.PhoneID)!;
            Assert.Equal("Reception", loaded.Name);
            Assert.Equal(extensionID, loaded.ExtensionID);

            this.phones.Delete(phone.PhoneID);
            Assert.Null(this.phones.GetByID(phone.PhoneID));
        }

        /// <summary>
        /// Deleting an extension leaves the phone known but unassigned rather than failing on the
        /// foreign key: the hardware is still on the desk (D80).
        /// </summary>
        [Fact]
        public void Deleting_an_extension_unassigns_the_phone_rather_than_failing()
        {
            var extensionID = this.AddExtension();
            var phone = this.phones.Register(Mac, "VVX_410", "5.9.5.0614", "10.8.20.31");

            phone.ExtensionID = extensionID;
            this.phones.Update(phone);

            this.extensions.Delete(extensionID);

            var loaded = this.phones.GetByMac(Mac)!;
            Assert.NotNull(loaded);
            Assert.Null(loaded.ExtensionID);
        }

        /// <summary>
        /// The local SIP port is the phone's ID plus a base, so two phones behind one NAT never
        /// choose the same source port (D81).
        /// </summary>
        [Fact]
        public void Each_phone_gets_a_local_sip_port_of_its_own()
        {
            var first = this.phones.Register(Mac, "VVX_410", "5.9.5.0614", "10.8.20.31");
            var second = this.phones.Register("0004f2112233", "VVX_410", "5.9.5.0614", "10.8.20.32");

            Assert.Equal(Phone.LocalPortBase + (int)first.PhoneID, first.LocalSipPort);
            Assert.NotEqual(first.LocalSipPort, second.LocalSipPort);
        }

        /// <summary>
        /// Nothing about a phone is rendered into /etc/asterisk, so a write here is not a config
        /// change and must not put the apply button up (D79).
        /// </summary>
        [Fact]
        public void No_write_raises_the_apply_marker()
        {
            var marker = new ConfigPendingMarker(this.database);
            marker.Clear();

            var phone = this.phones.Register(Mac, "VVX_410", "5.9.5.0614", "10.8.20.31");
            Assert.False(marker.IsPending);

            phone.Name = "Reception";
            this.phones.Update(phone);
            Assert.False(marker.IsPending);

            this.phones.Delete(phone.PhoneID);
            Assert.False(marker.IsPending);
        }

        [Fact]
        public void A_mac_as_a_person_writes_it_is_normalised()
        {
            Assert.Equal(Mac, Phone.NormalizeMac("00:04:F2:AA:BB:CC"));
            Assert.Equal(Mac, Phone.NormalizeMac("00-04-f2-aa-bb-cc"));
            Assert.Equal(Mac, Phone.NormalizeMac(Mac));

            // Not a MAC at all is left alone, so validation reports it rather than this hiding it.
            Assert.Equal("nonsense", Phone.NormalizeMac(" nonsense "));
        }
    }
}
