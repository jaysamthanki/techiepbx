using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The assignable keys on a phone (D121): saving them replaces the whole set, a key can only
    /// point at something that exists, and the keys go when the phone does.
    /// </summary>
    public class PhoneButtonRepositoryTests : IDisposable
    {
        private const string Mac = "0004f2aabbcc";

        private readonly PhoneButtonRepository buttons;
        private readonly Database database;
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-phone-buttons-").FullName;
        private readonly ExtensionRepository extensions;
        private readonly PhoneRepository phones;

        public PhoneButtonRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.buttons = new PhoneButtonRepository(this.database);
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

        private long AddPhone() =>
            this.phones.Register(Mac, "VVX_410", "5.9.5.0614", "10.8.20.31").PhoneID;

        private static PhoneButton Key(int position, string targetType, string targetValue) =>
            new() { Position = position, TargetType = targetType, TargetValue = targetValue };

        [Fact]
        public void Keys_are_saved_and_read_back_in_key_order()
        {
            this.AddExtension();
            var phoneID = this.AddPhone();

            this.buttons.Replace(phoneID, new List<PhoneButton>
            {
                Key(4, PhoneButtonTarget.ParkingSlot, "3"),
                Key(1, PhoneButtonTarget.Extension, "1001"),
            });

            var loaded = this.buttons.GetForPhone(phoneID);

            Assert.Equal(2, loaded.Count);
            Assert.Equal(1, loaded[0].Position);
            Assert.Equal("Extension:1001", loaded[0].Key);
            Assert.Equal(4, loaded[1].Position);
            Assert.Equal("ParkingSlot:3", loaded[1].Key);
        }

        /// <summary>
        /// A save replaces every key, which is how one is cleared: the form posts all eight and
        /// the ones left on "Nothing" are simply not among them (D121).
        /// </summary>
        [Fact]
        public void Saving_replaces_every_key_rather_than_merging()
        {
            this.AddExtension();
            var phoneID = this.AddPhone();

            this.buttons.Replace(phoneID, new List<PhoneButton> { Key(1, PhoneButtonTarget.Extension, "1001") });
            this.buttons.Replace(phoneID, new List<PhoneButton> { Key(2, PhoneButtonTarget.ParkingSlot, "1") });

            var loaded = this.buttons.GetForPhone(phoneID);

            Assert.Single(loaded);
            Assert.Equal(2, loaded[0].Position);

            this.buttons.Replace(phoneID, new List<PhoneButton>());
            Assert.Empty(this.buttons.GetForPhone(phoneID));
        }

        [Fact]
        public void A_key_on_an_extension_that_does_not_exist_is_refused()
        {
            var phoneID = this.AddPhone();

            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.buttons.Replace(phoneID, new List<PhoneButton> { Key(1, PhoneButtonTarget.Extension, "1009") }));

            Assert.Contains("not there any more", ex.Message);
        }

        [Theory]
        [InlineData(0, PhoneButtonTarget.Extension, "1001")]
        [InlineData(9, PhoneButtonTarget.Extension, "1001")]
        [InlineData(1, "CallFlowControl", "1")]
        [InlineData(1, PhoneButtonTarget.ParkingSlot, "0")]
        [InlineData(1, PhoneButtonTarget.ParkingSlot, "10")]
        public void A_key_that_is_not_one_of_ours_is_refused(int position, string targetType, string targetValue)
        {
            this.AddExtension();
            var phoneID = this.AddPhone();

            Assert.Throws<ValidationFailedException>(() =>
                this.buttons.Replace(phoneID, new List<PhoneButton> { Key(position, targetType, targetValue) }));
        }

        [Fact]
        public void Two_keys_cannot_be_in_the_same_place()
        {
            this.AddExtension();
            this.AddExtension("1002");
            var phoneID = this.AddPhone();

            var ex = Assert.Throws<ValidationFailedException>(() => this.buttons.Replace(phoneID, new List<PhoneButton>
            {
                Key(1, PhoneButtonTarget.Extension, "1001"),
                Key(1, PhoneButtonTarget.Extension, "1002"),
            }));

            Assert.Contains("same place", ex.Message);
        }

        /// <summary>A key has no life of its own once the phone it is on has gone.</summary>
        [Fact]
        public void Deleting_the_phone_deletes_its_keys()
        {
            this.AddExtension();
            var phoneID = this.AddPhone();

            this.buttons.Replace(phoneID, new List<PhoneButton> { Key(1, PhoneButtonTarget.Extension, "1001") });
            this.phones.Delete(phoneID);

            Assert.Empty(this.buttons.GetForPhone(phoneID));
        }

        /// <summary>
        /// Nothing about a phone's keys is rendered into /etc/asterisk — the hints the lamps watch
        /// are there whether a key points at them or not — so a write here must not put the apply
        /// button up (D79, D121).
        /// </summary>
        [Fact]
        public void No_write_raises_the_apply_marker()
        {
            this.AddExtension();
            var phoneID = this.AddPhone();

            var marker = new ConfigPendingMarker(this.database);
            marker.Clear();

            this.buttons.Replace(phoneID, new List<PhoneButton> { Key(1, PhoneButtonTarget.Extension, "1001") });

            Assert.False(marker.IsPending);
        }

        /// <summary>
        /// What the provisioning endpoint drops before it writes a file: a key whose extension has
        /// been switched off, and a slot the lot does not have (parking off, or fewer slots than
        /// it once had). Either would be a lamp that can never light.
        /// </summary>
        [Fact]
        public void Usable_drops_keys_whose_target_has_gone()
        {
            var all = new List<Extension>
            {
                new() { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
                new() { Number = "1002", Name = "Sales", Secret = "EEEEffffGGGGhhhh2222", Enabled = false },
            };

            var assigned = new List<PhoneButton>
            {
                Key(1, PhoneButtonTarget.Extension, "1001"),
                Key(2, PhoneButtonTarget.Extension, "1002"),
                Key(3, PhoneButtonTarget.Extension, "1009"),
                Key(4, PhoneButtonTarget.ParkingSlot, "2"),
                Key(5, PhoneButtonTarget.ParkingSlot, "9"),
            };

            var usable = PhoneButton.Usable(assigned, all, new[] { 1, 2, 3 });

            Assert.Equal(new[] { 1, 4 }, usable.Select(b => b.Position));

            // Parking switched off is no slots at all, so no slot key survives.
            Assert.Equal(new[] { 1 }, PhoneButton.Usable(assigned, all, Array.Empty<int>()).Select(b => b.Position));
        }

        /// <summary>The select posts what the model wrote, and reads back as the same key.</summary>
        [Theory]
        [InlineData("Extension:1001", PhoneButtonTarget.Extension, "1001")]
        [InlineData("ParkingSlot:3", PhoneButtonTarget.ParkingSlot, "3")]
        public void A_posted_key_is_read_back_as_the_key_it_names(string posted, string targetType, string targetValue)
        {
            Assert.True(PhoneButton.TryParse(posted, 1, out var button));

            Assert.Equal(targetType, button.TargetType);
            Assert.Equal(targetValue, button.TargetValue);
            Assert.Equal(posted, button.Key);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Extension")]
        [InlineData("Extension:")]
        [InlineData("CallFlowControl:1")]
        [InlineData("ParkingSlot:12")]
        [InlineData("Hangup")]
        public void Anything_else_is_not_a_key(string? posted)
        {
            Assert.False(PhoneButton.TryParse(posted, 1, out _));
        }
    }
}
