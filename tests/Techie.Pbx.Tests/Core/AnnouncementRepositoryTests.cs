using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Announcement validation, including the half that needs the database: a play extension is
    /// dialled out of the same context as everything else, so it has to be a number nothing has
    /// already claimed (D57).
    /// </summary>
    public class AnnouncementRepositoryTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-announcements-").FullName;
        private readonly Database database;
        private readonly AnnouncementRepository announcements;
        private readonly ExtensionRepository extensions;
        private readonly RingGroupRepository groups;

        public AnnouncementRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.announcements = new AnnouncementRepository(this.database);
            this.extensions = new ExtensionRepository(this.database);
            this.groups = new RingGroupRepository(this.database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private static Announcement Announcement(string name = "Welcome message", string playExtension = "700") => new()
        {
            Name = name,
            Description = "Played to callers before the menu",
            PlayExtension = playExtension,
            AudioFile = "welcome-message.wav",
        };

        private void AddExtension(string number) =>
            this.extensions.Insert(new Extension
            {
                Number = number,
                Name = "Phone " + number,
                Secret = "AAAAbbbbCCCCdddd1111",
            });

        [Fact]
        public void Insert_then_read_back()
        {
            var id = this.announcements.Insert(Announcement());
            var loaded = this.announcements.GetByID(id)!;

            Assert.Equal("Welcome message", loaded.Name);
            Assert.Equal("Played to callers before the menu", loaded.Description);
            Assert.Equal("700", loaded.PlayExtension);
            Assert.Equal("welcome-message.wav", loaded.AudioFile);
            Assert.True(loaded.Enabled);
            Assert.True(loaded.IsPlayable);
            Assert.Equal("Announcement:700", loaded.ToDestination().Key);
        }

        [Fact]
        public void Two_announcements_cannot_share_a_name()
        {
            this.announcements.Insert(Announcement());

            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.announcements.Insert(Announcement(playExtension: "701")));

            Assert.Contains("already exists", ex.Message);
        }

        [Fact]
        public void A_name_is_required()
        {
            var ex = Assert.Throws<ValidationFailedException>(() => this.announcements.Insert(Announcement(name: " ")));

            Assert.Contains("Name is required", ex.Message);
        }

        /// <summary>The number is optional: an announcement can exist purely to be recorded first.</summary>
        [Fact]
        public void A_blank_play_extension_is_allowed_and_is_not_a_destination()
        {
            var id = this.announcements.Insert(Announcement(playExtension: ""));
            var loaded = this.announcements.GetByID(id)!;

            Assert.Equal("", loaded.PlayExtension);
            Assert.False(loaded.IsPlayable);
            Assert.DoesNotContain(
                DestinationCatalog.All(new List<Extension>(), new List<RingGroup>(), new[] { loaded }),
                c => c.Destination.Type == DestinationType.Announcement);
        }

        [Theory]
        [InlineData("7")]
        [InlineData("7000000")]
        [InlineData("70a")]
        [InlineData("*43")]
        [InlineData("*97")]
        public void A_play_extension_that_is_not_two_to_six_digits_is_refused(string playExtension)
        {
            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.announcements.Insert(Announcement(playExtension: playExtension)));

            Assert.Contains("Play extension", ex.Message);
        }

        /// <summary>
        /// Feature codes all start with '*' and a play extension is digits only, so the two cannot
        /// meet — which is what the theory above pins down for *43 and *97 specifically.
        /// </summary>
        [Fact]
        public void A_number_an_extension_already_uses_is_refused()
        {
            AddExtension("700");

            var ex = Assert.Throws<ValidationFailedException>(() => this.announcements.Insert(Announcement()));

            Assert.Contains("Extension 700 already uses that number", ex.Message);
        }

        [Fact]
        public void A_number_a_ring_group_already_uses_is_refused()
        {
            AddExtension("1001");
            this.groups.Insert(new RingGroup { Number = "700", Name = "Support", Members = "1001" });

            var ex = Assert.Throws<ValidationFailedException>(() => this.announcements.Insert(Announcement()));

            Assert.Contains("Ring group 700 already uses that number", ex.Message);
        }

        [Fact]
        public void A_number_another_announcement_already_plays_on_is_refused()
        {
            this.announcements.Insert(Announcement());

            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.announcements.Insert(Announcement(name: "Holiday closure")));

            Assert.Contains("already plays on 700", ex.Message);
        }

        /// <summary>The collision check has to let a row keep the number it already has.</summary>
        [Fact]
        public void An_announcement_can_be_updated_without_colliding_with_itself()
        {
            var id = this.announcements.Insert(Announcement());

            var loaded = this.announcements.GetByID(id)!;
            loaded.Description = "Reworded";
            this.announcements.Update(loaded);

            Assert.Equal("Reworded", this.announcements.GetByID(id)!.Description);
        }

        /// <summary>
        /// The check announcements make against ring groups runs the other way too, or the hole
        /// stays open from whichever side happens to be created second (D57).
        /// </summary>
        [Fact]
        public void A_ring_group_cannot_take_a_number_an_announcement_plays_on()
        {
            AddExtension("1001");
            this.announcements.Insert(Announcement());

            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.groups.Insert(new RingGroup { Number = "700", Name = "Support", Members = "1001" }));

            Assert.Contains("already plays on 700", ex.Message);
        }

        /// <summary>
        /// The stored file name is derived from the announcement name, never from the browser's,
        /// and a row naming anything else is refused before it can reach a conf file.
        /// </summary>
        [Theory]
        [InlineData("../../escape.wav")]
        [InlineData("Welcome Message.wav")]
        [InlineData("welcome.mp3")]
        public void A_stored_file_name_we_would_not_have_written_is_refused(string audioFile)
        {
            var announcement = Announcement();
            announcement.AudioFile = audioFile;

            var ex = Assert.Throws<ValidationFailedException>(() => this.announcements.Insert(announcement));

            Assert.Contains("not one this system would have written", ex.Message);
        }

        [Fact]
        public void Update_and_delete()
        {
            var announcement = Announcement();
            this.announcements.Insert(announcement);

            announcement.Name = "Renamed";
            announcement.Enabled = false;
            this.announcements.Update(announcement);

            var loaded = this.announcements.GetByID(announcement.AnnouncementID)!;
            Assert.Equal("Renamed", loaded.Name);
            Assert.False(loaded.Enabled);

            this.announcements.Delete(announcement.AnnouncementID);
            Assert.Null(this.announcements.GetByID(announcement.AnnouncementID));
        }

        [Fact]
        public void Every_write_raises_the_apply_marker()
        {
            var marker = new ConfigPendingMarker(this.database);
            marker.Clear();

            var id = this.announcements.Insert(Announcement());
            Assert.True(marker.IsPending);

            marker.Clear();
            this.announcements.Delete(id);
            Assert.True(marker.IsPending);
        }

        /// <summary>
        /// An inbound route can be pointed at an announcement, which is the whole reason the
        /// destination type exists (D56).
        /// </summary>
        [Fact]
        public void An_announcement_is_offered_as_a_destination_once_it_can_play()
        {
            var id = this.announcements.Insert(Announcement());
            var loaded = this.announcements.GetByID(id)!;

            var choice = DestinationCatalog.Find(
                new List<Extension>(), new List<RingGroup>(), new[] { loaded },
                new Destination(DestinationType.Announcement, "700"));

            Assert.NotNull(choice);
            Assert.Equal("700 Welcome message", choice.Label);
            Assert.Equal(DestinationCatalog.AnnouncementsGroup, choice.GroupName);
        }
    }
}
