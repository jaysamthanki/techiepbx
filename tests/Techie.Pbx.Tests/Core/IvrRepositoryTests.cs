using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// IVR validation, including the halves that need the database: the greeting has to be an
    /// announcement with audio (D58), the play extension has to be a number nothing else has
    /// claimed (D57), and a menu must not send a silent caller round in a circle (D59).
    /// </summary>
    public class IvrRepositoryTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-ivrs-").FullName;
        private readonly Database database;
        private readonly AnnouncementRepository announcements;
        private readonly ExtensionRepository extensions;
        private readonly RingGroupRepository groups;
        private readonly IvrRepository ivrs;
        private readonly long greetingID;

        public IvrRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.announcements = new AnnouncementRepository(this.database);
            this.extensions = new ExtensionRepository(this.database);
            this.groups = new RingGroupRepository(this.database);
            this.ivrs = new IvrRepository(this.database);

            this.greetingID = this.announcements.Insert(new Announcement
            {
                Name = "Menu greeting",
                AudioFile = "menu-greeting.wav",
            });
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private Ivr Ivr(string name = "Main menu", string playExtension = "500") => new()
        {
            Name = name,
            Description = "Daytime auto attendant",
            AnnouncementID = this.greetingID,
            PlayExtension = playExtension,
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
            var ivr = Ivr();
            ivr.Entries.Add(new IvrEntry { Digit = "1", DestinationType = "Hangup" });

            var id = this.ivrs.Insert(ivr);
            var loaded = this.ivrs.GetByID(id)!;

            Assert.Equal("Main menu", loaded.Name);
            Assert.Equal("Daytime auto attendant", loaded.Description);
            Assert.Equal(this.greetingID, loaded.AnnouncementID);
            Assert.Equal("500", loaded.PlayExtension);
            Assert.Equal(10, loaded.TimeoutSeconds);
            Assert.Equal(3, loaded.Retries);
            Assert.False(loaded.EnableDirectDial);
            Assert.True(loaded.Enabled);
            Assert.Equal("Hangup", loaded.ToFinalDestination().Key);
            Assert.Equal("Ivr:500", loaded.ToDestination().Key);
            Assert.Equal("ivr-" + id, loaded.Context);

            var entry = Assert.Single(loaded.Entries);
            Assert.Equal("1", entry.Digit);
            Assert.Equal(id, entry.IvrID);
        }

        [Fact]
        public void Two_ivrs_cannot_share_a_name()
        {
            this.ivrs.Insert(Ivr());

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(Ivr(playExtension: "501")));

            Assert.Contains("already exists", ex.Message);
        }

        [Fact]
        public void A_greeting_is_required()
        {
            var ivr = Ivr();
            ivr.AnnouncementID = 0;

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(ivr));

            Assert.Contains("needs an announcement", ex.Message);
        }

        [Fact]
        public void A_greeting_that_does_not_exist_is_refused()
        {
            var ivr = Ivr();
            ivr.AnnouncementID = 987;

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(ivr));

            Assert.Contains("Choose an announcement", ex.Message);
        }

        /// <summary>
        /// A greeting with nothing to play would answer the call and then sit in silence, so it is
        /// refused where the admin can see it rather than discovered on a real call (D58).
        /// </summary>
        [Fact]
        public void A_greeting_with_no_audio_is_refused()
        {
            var silent = this.announcements.Insert(new Announcement { Name = "Not recorded yet" });

            var ivr = Ivr();
            ivr.AnnouncementID = silent;

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(ivr));

            Assert.Contains("has no audio yet", ex.Message);
        }

        [Fact]
        public void A_greeting_that_is_switched_off_is_refused()
        {
            var greeting = this.announcements.GetByID(this.greetingID)!;
            greeting.Enabled = false;
            this.announcements.Update(greeting);

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(Ivr()));

            Assert.Contains("switched off", ex.Message);
        }

        /// <summary>
        /// The greeting is a reference, so the announcement it points at cannot simply vanish
        /// (D58). The foreign key catches it; the message says what to do about it.
        /// </summary>
        [Fact]
        public void An_announcement_an_ivr_greets_with_cannot_be_deleted()
        {
            this.ivrs.Insert(Ivr());

            var ex = Assert.Throws<ValidationFailedException>(() => this.announcements.Delete(this.greetingID));

            Assert.Contains("still an IVR's greeting", ex.Message);
        }

        /// <summary>The number is optional: an IVR can be built before it is given one.</summary>
        [Fact]
        public void A_blank_play_extension_is_allowed_and_is_not_a_destination()
        {
            var id = this.ivrs.Insert(Ivr(playExtension: ""));
            var loaded = this.ivrs.GetByID(id)!;

            Assert.False(loaded.IsPlayable);
            Assert.DoesNotContain(
                DestinationCatalog.All(
                    new List<Extension>(), new List<RingGroup>(), this.announcements.GetAll(), new[] { loaded }),
                c => c.Destination.Type == DestinationType.Ivr);
        }

        [Theory]
        [InlineData("5")]
        [InlineData("5000000")]
        [InlineData("50a")]
        [InlineData("*43")]
        public void A_play_extension_that_is_not_two_to_six_digits_is_refused(string playExtension)
        {
            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(Ivr(playExtension: playExtension)));

            Assert.Contains("Play extension", ex.Message);
        }

        [Fact]
        public void A_number_an_extension_already_uses_is_refused()
        {
            AddExtension("500");

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(Ivr()));

            Assert.Contains("Extension 500 already uses that number", ex.Message);
        }

        [Fact]
        public void A_number_a_ring_group_already_uses_is_refused()
        {
            AddExtension("1001");
            this.groups.Insert(new RingGroup { Number = "500", Name = "Support", Members = "1001" });

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(Ivr()));

            Assert.Contains("Ring group 500 already uses that number", ex.Message);
        }

        [Fact]
        public void A_number_an_announcement_already_plays_on_is_refused()
        {
            this.announcements.Insert(new Announcement
            {
                Name = "Holiday closure",
                PlayExtension = "500",
                AudioFile = "holiday-closure.wav",
            });

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(Ivr()));

            Assert.Contains("already plays on 500", ex.Message);
        }

        [Fact]
        public void A_number_another_ivr_already_plays_on_is_refused()
        {
            this.ivrs.Insert(Ivr());

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(Ivr(name: "After hours")));

            Assert.Contains("IVR 'Main menu' already plays on 500", ex.Message);
        }

        /// <summary>
        /// The checks an IVR makes run the other way too, or the hole stays open from whichever
        /// side happens to be created second (D57).
        /// </summary>
        [Fact]
        public void A_ring_group_cannot_take_a_number_an_ivr_plays_on()
        {
            AddExtension("1001");
            this.ivrs.Insert(Ivr());

            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.groups.Insert(new RingGroup { Number = "500", Name = "Support", Members = "1001" }));

            Assert.Contains("IVR 'Main menu' already plays on 500", ex.Message);
        }

        [Fact]
        public void An_announcement_cannot_take_a_number_an_ivr_plays_on()
        {
            this.ivrs.Insert(Ivr());

            var ex = Assert.Throws<ValidationFailedException>(() => this.announcements.Insert(new Announcement
            {
                Name = "Holiday closure",
                PlayExtension = "500",
                AudioFile = "holiday-closure.wav",
            }));

            Assert.Contains("IVR 'Main menu' already plays on 500", ex.Message);
        }

        /// <summary>The collision check has to let a row keep the number it already has.</summary>
        [Fact]
        public void An_ivr_can_be_updated_without_colliding_with_itself()
        {
            var id = this.ivrs.Insert(Ivr());

            var loaded = this.ivrs.GetByID(id)!;
            loaded.Description = "Reworded";
            this.ivrs.Update(loaded);

            Assert.Equal("Reworded", this.ivrs.GetByID(id)!.Description);
        }

        [Theory]
        [InlineData("A")]
        [InlineData("12")]
        [InlineData("")]
        public void A_key_that_is_not_on_a_keypad_is_refused(string digit)
        {
            var ivr = Ivr();
            ivr.Entries.Add(new IvrEntry { Digit = digit, DestinationType = "Hangup" });

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(ivr));

            Assert.Contains("is not a key a caller can press", ex.Message);
        }

        [Fact]
        public void The_same_key_cannot_appear_twice()
        {
            var ivr = Ivr();
            ivr.Entries.Add(new IvrEntry { Digit = "1", DestinationType = "Hangup" });
            ivr.Entries.Add(new IvrEntry { Digit = "1", DestinationType = "Hangup" });

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(ivr));

            Assert.Contains("only appear once", ex.Message);
        }

        [Fact]
        public void A_key_pointing_at_something_that_is_gone_is_refused()
        {
            var ivr = Ivr();
            ivr.Entries.Add(new IvrEntry { Digit = "1", DestinationType = "Extension", DestinationValue = "1009" });

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(ivr));

            Assert.Contains("Where key 1 goes is not there any more", ex.Message);
        }

        [Fact]
        public void A_final_destination_that_is_gone_is_refused()
        {
            var ivr = Ivr();
            ivr.DestinationType = "Extension";
            ivr.DestinationValue = "1009";

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(ivr));

            Assert.Contains("not there any more", ex.Message);
        }

        /// <summary>
        /// A caller who says nothing would otherwise be handed round between menus for ever, so
        /// the chain is followed before the row is written (D59).
        /// </summary>
        [Fact]
        public void An_ivr_cannot_give_up_to_itself()
        {
            var ivr = Ivr();
            ivr.DestinationType = "Ivr";
            ivr.DestinationValue = "500";

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(ivr));

            Assert.Contains("back to itself", ex.Message);
        }

        [Fact]
        public void An_ivr_cannot_give_up_to_a_menu_that_gives_up_back_to_it()
        {
            var first = this.ivrs.Insert(Ivr());
            this.ivrs.Insert(Ivr(name: "After hours", playExtension: "501"));

            var second = this.ivrs.GetAll().Single(i => i.PlayExtension == "501");
            second.DestinationType = "Ivr";
            second.DestinationValue = "500";
            this.ivrs.Update(second);

            var loaded = this.ivrs.GetByID(first)!;
            loaded.DestinationType = "Ivr";
            loaded.DestinationValue = "501";

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Update(loaded));

            Assert.Contains("comes back round to this menu", ex.Message);
        }

        /// <summary>
        /// A key that repeats the menu is a caller asking to hear it again, which only happens
        /// when someone presses it — so it is allowed where a silent loop is not (D59).
        /// </summary>
        [Fact]
        public void A_key_may_point_back_at_its_own_menu()
        {
            var ivr = Ivr();
            ivr.Entries.Add(new IvrEntry { Digit = "9", DestinationType = "Ivr", DestinationValue = "500" });

            var id = this.ivrs.Insert(ivr);

            Assert.Equal("Ivr:500", this.ivrs.GetByID(id)!.Entries.Single().ToDestination().Key);
        }

        [Fact]
        public void The_digit_map_is_replaced_on_update_and_comes_back_in_keypad_order()
        {
            var ivr = Ivr();
            ivr.Entries.Add(new IvrEntry { Digit = "1", DestinationType = "Hangup" });
            var id = this.ivrs.Insert(ivr);

            var loaded = this.ivrs.GetByID(id)!;
            loaded.Entries = new List<IvrEntry>
            {
                new() { Digit = "*", DestinationType = "Hangup" },
                new() { Digit = "2", DestinationType = "Hangup" },
                new() { Digit = "0", DestinationType = "Hangup" },
            };
            this.ivrs.Update(loaded);

            Assert.Equal(new[] { "0", "2", "*" }, this.ivrs.GetByID(id)!.Entries.Select(e => e.Digit));
        }

        [Fact]
        public void Deleting_an_ivr_deletes_its_keys()
        {
            var ivr = Ivr();
            ivr.Entries.Add(new IvrEntry { Digit = "1", DestinationType = "Hangup" });
            var id = this.ivrs.Insert(ivr);

            this.ivrs.Delete(id);

            Assert.Null(this.ivrs.GetByID(id));
            Assert.Empty(this.ivrs.GetAll());

            // The greeting was only referenced, so it outlives the menu.
            Assert.NotNull(this.announcements.GetByID(this.greetingID));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(61)]
        public void A_timeout_outside_the_allowed_range_is_refused(int seconds)
        {
            var ivr = Ivr();
            ivr.TimeoutSeconds = seconds;

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(ivr));

            Assert.Contains("Timeout must be between", ex.Message);
        }

        [Fact]
        public void More_retries_than_anyone_would_sit_through_is_refused()
        {
            var ivr = Ivr();
            ivr.Retries = 11;

            var ex = Assert.Throws<ValidationFailedException>(() => this.ivrs.Insert(ivr));

            Assert.Contains("Retries must be between", ex.Message);
        }

        [Fact]
        public void Every_write_raises_the_apply_marker()
        {
            var marker = new ConfigPendingMarker(this.database);
            marker.Clear();

            var id = this.ivrs.Insert(Ivr());
            Assert.True(marker.IsPending);

            marker.Clear();
            this.ivrs.Delete(id);
            Assert.True(marker.IsPending);
        }

        /// <summary>
        /// An inbound route, a ring group's failover or another menu's key can be pointed at an
        /// IVR, which is what the destination type is for (D59).
        /// </summary>
        [Fact]
        public void An_ivr_is_offered_as_a_destination_once_it_can_play()
        {
            var id = this.ivrs.Insert(Ivr());
            var loaded = this.ivrs.GetByID(id)!;

            var choice = DestinationCatalog.Find(
                new List<Extension>(), new List<RingGroup>(), this.announcements.GetAll(), new[] { loaded },
                new Destination(DestinationType.Ivr, "500"));

            Assert.NotNull(choice);
            Assert.Equal("500 Main menu", choice.Label);
            Assert.Equal(DestinationCatalog.IvrsGroup, choice.GroupName);
        }

        /// <summary>
        /// An IVR whose greeting has gone quiet is not in the dialplan, so it is not a choice
        /// either: offering a dead end is worse than not offering it (D58).
        /// </summary>
        [Fact]
        public void An_ivr_whose_greeting_is_switched_off_is_not_offered_as_a_destination()
        {
            var id = this.ivrs.Insert(Ivr());

            var greeting = this.announcements.GetByID(this.greetingID)!;
            greeting.Enabled = false;
            this.announcements.Update(greeting);

            Assert.Null(DestinationCatalog.Find(
                new List<Extension>(), new List<RingGroup>(), this.announcements.GetAll(),
                new[] { this.ivrs.GetByID(id)! }, new Destination(DestinationType.Ivr, "500")));
        }
    }
}
