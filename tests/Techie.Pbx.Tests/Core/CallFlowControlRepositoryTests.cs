using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Call flow control validation (F9), including the halves that need the database: the code
    /// must not be one the system already dials on or another switch's, both destinations have to
    /// be somewhere in the catalog, and two switches must not hand a call round in a circle.
    /// </summary>
    public class CallFlowControlRepositoryTests : IDisposable
    {
        private readonly CallFlowControlRepository controls;
        private readonly Database database;
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-cfc-").FullName;
        private readonly ExtensionRepository extensions;

        public CallFlowControlRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.controls = new CallFlowControlRepository(this.database);
            this.extensions = new ExtensionRepository(this.database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private void AddExtension(string number, bool voicemail = false) =>
            this.extensions.Insert(new Extension
            {
                Number = number,
                Name = "Phone " + number,
                Secret = "AAAAbbbbCCCCdddd1111",
                VoicemailEnabled = voicemail,
                VoicemailPin = voicemail ? "4321" : "",
            });

        /// <summary>A switch that is valid on an empty database: both ways end in Hangup.</summary>
        private static CallFlowControl Control(string code = "*28", string name = "Night mode") => new()
        {
            Name = name,
            FeatureCode = code,
        };

        [Fact]
        public void Insert_then_read_back()
        {
            this.AddExtension("1001");
            this.AddExtension("1002", voicemail: true);

            var id = this.controls.Insert(new CallFlowControl
            {
                Name = "Night mode",
                FeatureCode = "*28",
                NormalDestinationType = "Extension",
                NormalDestinationValue = "1001",
                OverrideDestinationType = "Voicemail",
                OverrideDestinationValue = "1002",
            });

            var loaded = this.controls.GetByID(id)!;

            Assert.Equal("Night mode", loaded.Name);
            Assert.Equal("*28", loaded.FeatureCode);
            Assert.Equal("Extension:1001", loaded.ToNormalDestination().Key);
            Assert.Equal("Voicemail:1002", loaded.ToOverrideDestination().Key);
            Assert.Equal($"cfc-{id}", loaded.Context);
            Assert.Equal($"CFC/{id}", loaded.StateKey);
            Assert.Equal($"Custom:tnpbx-cfc-{id}", loaded.DeviceName);
        }

        [Fact]
        public void Get_all_is_in_name_order()
        {
            this.controls.Insert(Control("*28", "Night mode"));
            this.controls.Insert(Control("*29", "Lunch"));

            Assert.Equal(new[] { "Lunch", "Night mode" }, this.controls.GetAll().Select(c => c.Name));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Night]\n[evil")]
        [InlineData("Night; mode")]
        public void A_name_that_is_missing_or_not_ours_is_refused(string name)
        {
            Assert.Throws<ValidationFailedException>(() => this.controls.Insert(Control(name: name)));
        }

        [Fact]
        public void A_name_longer_than_64_characters_is_refused()
        {
            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Insert(Control(name: new string('a', 65))));

            Assert.Contains("64 characters", ex.Message);

            this.controls.Insert(Control(name: new string('a', 64)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("28")]
        [InlineData("*2")]
        [InlineData("*2845")]
        [InlineData("#28")]
        [InlineData("*2a")]
        [InlineData("*28\n")]
        public void A_feature_code_that_is_not_a_star_and_two_or_three_digits_is_refused(string code)
        {
            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Insert(Control(code)));

            Assert.Contains("Feature code must be", ex.Message);
        }

        [Theory]
        [InlineData("*28")]
        [InlineData("*280")]
        public void Two_or_three_digits_after_the_star_are_both_fine(string code)
        {
            var id = this.controls.Insert(Control(code));

            Assert.Equal(code, this.controls.GetByID(id)!.FeatureCode);
        }

        /// <summary>
        /// A code the dialplan already answers would shadow one or the other, so it is refused with
        /// the reason. The codes come from <see cref="SystemCodes"/>, the list the renderers write.
        /// </summary>
        [Theory]
        [InlineData(SystemCodes.EchoTest, "echo test")]
        [InlineData(SystemCodes.VoicemailMain, "voicemail")]
        [InlineData(SystemCodes.PickupPrefix + "1", "call pickup")]
        [InlineData(SystemCodes.PickupPrefix + "12", "call pickup")]
        public void A_code_the_system_already_dials_on_is_refused(string code, string reason)
        {
            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Insert(Control(code)));

            Assert.Contains(reason, ex.Message);
        }

        /// <summary>
        /// The attended transfer is a star and one digit, which a switch's code can never be; the
        /// shape rule refuses it before the clash would.
        /// </summary>
        [Fact]
        public void The_attended_transfer_code_cannot_be_a_switch_either()
        {
            Assert.Throws<ValidationFailedException>(() => this.controls.Insert(Control(SystemCodes.AttendedTransfer)));
        }

        /// <summary>
        /// The park code is a setting, so the clash follows whatever it is set to today (D119), and
        /// a code that was the park code once is free again once the setting moves.
        /// </summary>
        [Fact]
        public void The_configured_park_code_is_refused()
        {
            var settings = new SettingsRepository(this.database);
            settings.Set(SettingsKeys.ParkingDtmfCode, "*35");

            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Insert(Control("*35")));

            Assert.Contains("parks a call", ex.Message);

            settings.Set(SettingsKeys.ParkingDtmfCode, "*36");
            this.controls.Insert(Control("*35"));
        }

        [Fact]
        public void Two_switches_cannot_share_a_code()
        {
            this.controls.Insert(Control("*28", "Night mode"));

            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Insert(Control("*28", "Lunch")));

            Assert.Contains("'Night mode' already uses *28", ex.Message);
        }

        [Fact]
        public void Two_switches_cannot_share_a_name()
        {
            this.controls.Insert(Control("*28", "Night mode"));

            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Insert(Control("*29", "Night mode")));

            Assert.Contains("already exists", ex.Message);
        }

        /// <summary>A switch saved again keeps its own code and name without tripping over itself.</summary>
        [Fact]
        public void A_switch_can_be_saved_again_under_its_own_code_and_name()
        {
            var control = Control();
            this.controls.Insert(control);

            this.controls.Update(control);

            Assert.Single(this.controls.GetAll());
        }

        [Fact]
        public void A_normal_destination_that_is_gone_is_refused()
        {
            var control = Control();
            control.NormalDestinationType = "Extension";
            control.NormalDestinationValue = "9999";

            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Insert(control));

            Assert.Contains("normally goes is not there any more", ex.Message);
            Assert.DoesNotContain("while switched on", ex.Message);
        }

        [Fact]
        public void An_override_destination_that_is_gone_is_refused()
        {
            this.AddExtension("1001");
            var control = Control();
            control.OverrideDestinationType = "Voicemail";
            control.OverrideDestinationValue = "1001";

            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Insert(control));

            Assert.Contains("while switched on is not there any more", ex.Message);
            Assert.DoesNotContain("normally goes", ex.Message);
        }

        /// <summary>Both are checked, not just the first: either one is where a real call ends up.</summary>
        [Fact]
        public void Both_destinations_are_checked()
        {
            var control = Control();
            control.NormalDestinationType = "Extension";
            control.NormalDestinationValue = "9998";
            control.OverrideDestinationType = "RingGroup";
            control.OverrideDestinationValue = "600";

            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Insert(control));

            Assert.Contains("normally goes is not there any more", ex.Message);
            Assert.Contains("while switched on is not there any more", ex.Message);
        }

        [Fact]
        public void A_destination_that_does_not_read_back_is_refused()
        {
            var control = Control();
            control.OverrideDestinationType = "Queue";
            control.OverrideDestinationValue = "1";

            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Insert(control));

            Assert.Contains("Choose where a call goes while it is switched on", ex.Message);
        }

        /// <summary>
        /// Both destinations are checked against the whole catalog, so every kind a picker offers
        /// can be saved on either side (D35, D136).
        /// </summary>
        [Fact]
        public void Either_destination_can_be_anything_in_the_catalog()
        {
            this.AddExtension("1001", voicemail: true);

            new RingGroupRepository(this.database).Insert(new RingGroup
            {
                Number = "600",
                Name = "Support",
                Members = "1001",
                Strategy = "All",
                RingSeconds = 20,
                DestinationType = "Hangup",
            });

            var announcementID = new AnnouncementRepository(this.database).Insert(new Announcement
            {
                Name = "Closed message",
                AudioFile = "closed-message.wav",
                PlayExtension = "7001",
            });

            new IvrRepository(this.database).Insert(new Ivr
            {
                Name = "Main menu",
                AnnouncementID = announcementID,
                PlayExtension = "7002",
            });

            new TimeConditionRepository(this.database).Insert(new TimeCondition
            {
                Name = "Office hours",
                PlayExtension = "7003",
            });

            this.controls.Insert(Control("*20", "Chained to"));

            var targets = new[]
            {
                ("Extension", "1001"),
                ("Voicemail", "1001"),
                ("RingGroup", "600"),
                ("Announcement", "7001"),
                ("Ivr", "7002"),
                ("TimeCondition", "7003"),
                ("CallFlowControl", "*20"),
                ("Hangup", ""),
            };

            var code = 30;
            foreach (var (type, value) in targets)
            {
                var control = Control($"*{code}", $"Switch {code}");
                code++;
                control.NormalDestinationType = type;
                control.NormalDestinationValue = value;
                control.OverrideDestinationType = type;
                control.OverrideDestinationValue = value;

                this.controls.Insert(control);

                var loaded = this.controls.GetByID(control.CallFlowControlID)!;
                Assert.Equal(control.NormalDestinationKey(), loaded.NormalDestinationKey());
                Assert.Equal(control.OverrideDestinationKey(), loaded.OverrideDestinationKey());
            }
        }

        /// <summary>One that exists but a call cannot reach is as gone as one that does not (D56).</summary>
        [Fact]
        public void A_destination_on_a_switched_off_ring_group_is_refused()
        {
            this.AddExtension("1001");

            new RingGroupRepository(this.database).Insert(new RingGroup
            {
                Number = "600",
                Name = "Support",
                Members = "1001",
                Strategy = "All",
                RingSeconds = 20,
                DestinationType = "Hangup",
                Enabled = false,
            });

            var control = Control();
            control.NormalDestinationType = "RingGroup";
            control.NormalDestinationValue = "600";

            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Insert(control));

            Assert.Contains("not there any more", ex.Message);
        }

        /// <summary>
        /// A switch pointed at a code no switch has is dangling like any other destination: the
        /// entry context has no door for it.
        /// </summary>
        [Fact]
        public void A_destination_on_a_switch_that_does_not_exist_is_refused()
        {
            var control = Control();
            control.OverrideDestinationType = "CallFlowControl";
            control.OverrideDestinationValue = "*99";

            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Insert(control));

            Assert.Contains("while switched on is not there any more", ex.Message);
        }

        /// <summary>Pointing at yourself is a call that never leaves the context, whichever way you are set.</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_switch_that_sends_a_call_back_to_itself_is_refused(bool normal)
        {
            var control = Control();
            if (normal)
            {
                control.NormalDestinationType = "CallFlowControl";
                control.NormalDestinationValue = "*28";
            }
            else
            {
                control.OverrideDestinationType = "CallFlowControl";
                control.OverrideDestinationValue = "*28";
            }

            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Insert(control));

            Assert.Contains("back to itself", ex.Message);
        }

        /// <summary>
        /// A cycle that only closes while one switch is on is still a call that never ends the day
        /// somebody flips it, so both sides of every switch are followed.
        /// </summary>
        [Fact]
        public void A_loop_round_two_switches_through_either_side_is_refused()
        {
            this.controls.Insert(Control("*28", "Night mode"));

            var second = Control("*29", "Lunch");
            second.OverrideDestinationType = "CallFlowControl";
            second.OverrideDestinationValue = "*28";
            this.controls.Insert(second);

            // *28 normally -> *29, which while switched on -> *28.
            var first = this.controls.GetAll().Single(c => c.FeatureCode == "*28");
            first.NormalDestinationType = "CallFlowControl";
            first.NormalDestinationValue = "*29";

            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Update(first));

            Assert.Contains("round for ever", ex.Message);
        }

        [Fact]
        public void A_chain_of_switches_that_ends_somewhere_is_fine()
        {
            this.controls.Insert(Control("*27", "Holiday"));

            var second = Control("*28", "Night mode");
            second.OverrideDestinationType = "CallFlowControl";
            second.OverrideDestinationValue = "*27";
            this.controls.Insert(second);

            var third = Control("*29", "Lunch");
            third.NormalDestinationType = "CallFlowControl";
            third.NormalDestinationValue = "*28";
            third.OverrideDestinationType = "CallFlowControl";
            third.OverrideDestinationValue = "*27";
            this.controls.Insert(third);

            Assert.Equal(3, this.controls.GetAll().Count);
        }

        [Fact]
        public void Updating_a_switch_that_does_not_exist_is_refused()
        {
            var control = Control();
            control.CallFlowControlID = 42;

            var ex = Assert.Throws<ValidationFailedException>(() => this.controls.Update(control));

            Assert.Contains("does not exist", ex.Message);
        }

        [Fact]
        public void Update_and_delete()
        {
            this.AddExtension("1001");
            var control = Control();
            this.controls.Insert(control);

            control.Name = "Closed early";
            control.FeatureCode = "*281";
            control.OverrideDestinationType = "Extension";
            control.OverrideDestinationValue = "1001";
            this.controls.Update(control);

            var loaded = this.controls.GetByID(control.CallFlowControlID)!;
            Assert.Equal("Closed early", loaded.Name);
            Assert.Equal("*281", loaded.FeatureCode);
            Assert.Equal("Extension:1001", loaded.OverrideDestinationKey());

            this.controls.Delete(control.CallFlowControlID);
            Assert.Null(this.controls.GetByID(control.CallFlowControlID));
        }

        /// <summary>The dialplan changes with a switch's shape, so every write puts the apply button up.</summary>
        [Fact]
        public void Every_write_raises_the_apply_marker()
        {
            var marker = new ConfigPendingMarker(this.database);
            marker.Clear();

            var control = Control();
            this.controls.Insert(control);
            Assert.True(marker.IsPending);

            marker.Clear();
            control.Name = "Renamed";
            this.controls.Update(control);
            Assert.True(marker.IsPending);

            marker.Clear();
            this.controls.Delete(control.CallFlowControlID);
            Assert.True(marker.IsPending);
        }
    }
}
