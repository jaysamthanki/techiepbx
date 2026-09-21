using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The assignable keys on a phone (D121): saving them replaces the whole set, a key can only
    /// point at something that exists, and the keys go when the phone does.
    ///
    /// Since schema 020 the keys also say what the phone registers as — key 1 is its line — so the
    /// rules about that are here too: a phone has to have one, lines lead, and two phones cannot
    /// register as the same extension.
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

        private long AddPhone(string mac = Mac) =>
            this.phones.Register(mac, "VVX_410", "5.9.5.0614", "10.8.20.31").PhoneID;

        private static PhoneButton Key(int position, string targetType, string targetValue) =>
            new() { Position = position, TargetType = targetType, TargetValue = targetValue };

        /// <summary>The line every one of these phones registers as, unless the test says otherwise.</summary>
        private static PhoneButton Line(string number = "1001") =>
            Key(PhoneButton.FirstPosition, PhoneButtonTarget.Line, number);

        [Fact]
        public void Keys_are_saved_and_read_back_in_key_order()
        {
            this.AddExtension();
            this.AddExtension("1002");
            var phoneID = this.AddPhone();

            this.buttons.Replace(phoneID, new List<PhoneButton>
            {
                Key(4, PhoneButtonTarget.ParkingSlot, "3"),
                Key(2, PhoneButtonTarget.Blf, "1002"),
                Line(),
            });

            var loaded = this.buttons.GetForPhone(phoneID);

            Assert.Equal(new[] { "Line:1001", "Blf:1002", "ParkingSlot:3" }, loaded.Select(b => b.Key));
            Assert.Equal(new[] { 1, 2, 4 }, loaded.Select(b => b.Position));
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

            this.buttons.Replace(phoneID, new List<PhoneButton> { Line(), Key(2, PhoneButtonTarget.ParkingSlot, "1") });
            this.buttons.Replace(phoneID, new List<PhoneButton> { Line() });

            var loaded = this.buttons.GetForPhone(phoneID);

            Assert.Single(loaded);
            Assert.Equal("Line:1001", loaded[0].Key);
        }

        /// <summary>
        /// A phone registers as something or it is not a phone, so the set with no line in it is
        /// the one thing a save will not have (schema 020). That is also why there is no
        /// "unassign": a phone nobody needs is disabled or deleted.
        /// </summary>
        [Fact]
        public void A_phone_has_to_have_a_line()
        {
            this.AddExtension("1002");
            var phoneID = this.AddPhone();

            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.buttons.Replace(phoneID, new List<PhoneButton> { Key(1, PhoneButtonTarget.Blf, "1002") }));

            Assert.Contains("Key 1 has to be the extension this phone registers as", ex.Message);

            Assert.Throws<ValidationFailedException>(() => this.buttons.Replace(phoneID, new List<PhoneButton>()));
        }

        /// <summary>
        /// A second registration takes the next key down, not a key further along: on the handset
        /// the lines are the leading keys whatever this table says, so a set that disagrees would
        /// not be the set an admin saved.
        /// </summary>
        [Fact]
        public void A_line_can_only_be_one_of_the_leading_keys()
        {
            this.AddExtension();
            this.AddExtension("1002");
            this.AddExtension("1003");
            var phoneID = this.AddPhone();

            // Two lines together is allowed: a phone may register twice.
            this.buttons.Replace(phoneID, new List<PhoneButton>
            {
                Line(),
                Key(2, PhoneButtonTarget.Line, "1002"),
                Key(3, PhoneButtonTarget.Blf, "1003"),
            });

            var ex = Assert.Throws<ValidationFailedException>(() => this.buttons.Replace(phoneID, new List<PhoneButton>
            {
                Line(),
                Key(2, PhoneButtonTarget.Blf, "1003"),
                Key(3, PhoneButtonTarget.Line, "1002"),
            }));

            Assert.Contains("only the first keys", ex.Message);
        }

        /// <summary>
        /// Two handsets signed in as one extension both ring, and only one of them is the one
        /// anybody expected. The rule followed the registration from Phones.ExtensionID onto the
        /// key (schema 020).
        /// </summary>
        [Fact]
        public void Two_phones_cannot_register_as_the_same_extension()
        {
            this.AddExtension();
            var first = this.AddPhone();
            var second = this.AddPhone("0004f2112233");

            this.buttons.Replace(first, new List<PhoneButton> { Line() });

            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.buttons.Replace(second, new List<PhoneButton> { Line() }));

            Assert.Contains("another phone already registers as extension 1001", ex.Message);

            // And the phone that has it can be saved again without tripping over itself.
            this.buttons.Replace(first, new List<PhoneButton> { Line() });
        }

        /// <summary>Another phone's <em>lamp</em> is not a claim on anything: everybody may watch 1001.</summary>
        [Fact]
        public void A_lamp_on_an_extension_another_phone_registers_as_is_fine()
        {
            this.AddExtension();
            this.AddExtension("1002");
            var first = this.AddPhone();
            var second = this.AddPhone("0004f2112233");

            this.buttons.Replace(first, new List<PhoneButton> { Line() });
            this.buttons.Replace(second, new List<PhoneButton>
            {
                Line("1002"),
                Key(2, PhoneButtonTarget.Blf, "1001"),
            });

            Assert.Equal(2, this.buttons.GetForPhone(second).Count);
        }

        [Fact]
        public void A_key_on_an_extension_that_does_not_exist_is_refused()
        {
            var phoneID = this.AddPhone();

            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.buttons.Replace(phoneID, new List<PhoneButton> { Line("1009") }));

            Assert.Contains("not there any more", ex.Message);
        }

        [Theory]
        [InlineData(0, PhoneButtonTarget.Line, "1001")]
        [InlineData(9, PhoneButtonTarget.Line, "1001")]
        [InlineData(1, "CallFlowControl", "1")]
        [InlineData(1, "Extension", "1001")]
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
                Line(),
                Key(2, PhoneButtonTarget.Blf, "1002"),
                Key(2, PhoneButtonTarget.Blf, "1001"),
            }));

            Assert.Contains("same place", ex.Message);
        }

        /// <summary>A key has no life of its own once the phone it is on has gone.</summary>
        [Fact]
        public void Deleting_the_phone_deletes_its_keys()
        {
            this.AddExtension();
            var phoneID = this.AddPhone();

            this.buttons.Replace(phoneID, new List<PhoneButton> { Line() });
            this.phones.Delete(phoneID);

            Assert.Empty(this.buttons.GetForPhone(phoneID));
            Assert.Empty(this.buttons.GetLines());
        }

        /// <summary>
        /// Which extension each phone registers as, which is what the phones table and the status
        /// page ask now that no column answers it (schema 020). Lines only: a lamp is not a
        /// registration.
        /// </summary>
        [Fact]
        public void Get_lines_is_every_phones_registration_and_nothing_else()
        {
            this.AddExtension();
            this.AddExtension("1002");
            var phoneID = this.AddPhone();

            this.buttons.Replace(phoneID, new List<PhoneButton>
            {
                Line(),
                Key(2, PhoneButtonTarget.Blf, "1002"),
            });

            var lines = this.buttons.GetLines();

            Assert.Single(lines);
            Assert.Equal(phoneID, lines[0].PhoneID);
            Assert.Equal("1001", PhoneButton.LineNumber(lines));
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

            this.buttons.Replace(phoneID, new List<PhoneButton> { Line() });

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
                Line(),
                Key(2, PhoneButtonTarget.Blf, "1002"),
                Key(3, PhoneButtonTarget.Blf, "1009"),
                Key(4, PhoneButtonTarget.ParkingSlot, "2"),
                Key(5, PhoneButtonTarget.ParkingSlot, "9"),
            };

            var usable = PhoneButton.Usable(assigned, all, new[] { 1, 2, 3 });

            Assert.Equal(new[] { 1, 4 }, usable.Select(b => b.Position));

            // Parking switched off is no slots at all, so no slot key survives.
            Assert.Equal(new[] { 1 }, PhoneButton.Usable(assigned, all, Array.Empty<int>()).Select(b => b.Position));
        }

        /// <summary>
        /// A phone whose line has been switched off has no registration, and a lamp with no
        /// registration behind it cannot subscribe or dial — so it is given no keys at all rather
        /// than keys that do nothing.
        /// </summary>
        [Fact]
        public void Usable_drops_every_key_when_the_line_has_gone()
        {
            var all = new List<Extension>
            {
                new() { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111", Enabled = false },
                new() { Number = "1002", Name = "Sales", Secret = "EEEEffffGGGGhhhh2222" },
            };

            var assigned = new List<PhoneButton> { Line(), Key(2, PhoneButtonTarget.Blf, "1002") };

            Assert.Empty(PhoneButton.Usable(assigned, all, new[] { 1 }));
        }

        /// <summary>The select posts what the model wrote, and reads back as the same key.</summary>
        [Theory]
        [InlineData("Line:1001", PhoneButtonTarget.Line, "1001")]
        [InlineData("Blf:1002", PhoneButtonTarget.Blf, "1002")]
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
        [InlineData("Line")]
        [InlineData("Line:")]
        [InlineData("Extension:1001")]
        [InlineData("CallFlowControl:1")]
        [InlineData("ParkingSlot:12")]
        [InlineData("Hangup")]
        public void Anything_else_is_not_a_key(string? posted)
        {
            Assert.False(PhoneButton.TryParse(posted, 1, out _));
        }
    }
}
