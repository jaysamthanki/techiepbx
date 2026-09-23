using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Renumbering takes every reference with it (piece 37): an IVR, ring group, extension or call
    /// flow control that gets a new number leaves nothing pointing at the old one — and a renumber
    /// that is refused, however far it got, leaves nothing rewritten at all.
    /// </summary>
    public class RenumberingTests : IDisposable
    {
        /// <summary>
        /// Every stored destination column pair, the same list the sweep walks, so the tests count
        /// references from the tables themselves rather than through any repository.
        /// </summary>
        private static readonly (string Table, string TypeColumn, string ValueColumn)[] DestinationColumns =
        {
            ("CallFlowControls", "NormalDestinationType", "NormalDestinationValue"),
            ("CallFlowControls", "OverrideDestinationType", "OverrideDestinationValue"),
            ("InboundRoutes", "DestinationType", "DestinationValue"),
            ("IvrEntries", "DestinationType", "DestinationValue"),
            ("Ivrs", "DestinationType", "DestinationValue"),
            ("RingGroups", "DestinationType", "DestinationValue"),
            ("TimeConditionRules", "DestinationType", "DestinationValue"),
            ("TimeConditions", "ClosedDestinationType", "ClosedDestinationValue"),
            ("TimeConditions", "HolidayDestinationType", "HolidayDestinationValue"),
            ("TimeConditions", "OpenDestinationType", "OpenDestinationValue"),
        };

        /// <summary>What <see cref="PointEverythingAt"/> writes: one reference in every column pair.</summary>
        private const int EveryColumn = 10;

        private readonly AnnouncementRepository announcements;
        private readonly PhoneButtonRepository buttons;
        private readonly CallFlowControlRepository controls;
        private readonly TimeConditionRepository conditions;
        private readonly Database database;
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-renumber-").FullName;
        private readonly ExtensionRepository extensions;
        private readonly long greetingID;
        private readonly RingGroupRepository groups;
        private readonly InboundRouteRepository inbound;
        private readonly IvrRepository ivrs;
        private readonly ConfigPendingMarker marker;
        private readonly PhoneRepository phones;
        private readonly long trunkID;

        public RenumberingTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.announcements = new AnnouncementRepository(this.database);
            this.buttons = new PhoneButtonRepository(this.database);
            this.conditions = new TimeConditionRepository(this.database);
            this.controls = new CallFlowControlRepository(this.database);
            this.extensions = new ExtensionRepository(this.database);
            this.groups = new RingGroupRepository(this.database);
            this.inbound = new InboundRouteRepository(this.database);
            this.ivrs = new IvrRepository(this.database);
            this.marker = new ConfigPendingMarker(this.database);
            this.phones = new PhoneRepository(this.database);

            this.AddExtension("1001");
            this.AddExtension("1002");

            this.greetingID = this.announcements.Insert(new Announcement { Name = "Menu greeting", AudioFile = "menu-greeting.wav" });

            this.trunkID = new TrunkRepository(this.database).Insert(new Trunk
            {
                Name = "callcentric",
                ServerHost = "callcentric.com",
                Username = "17771234567",
                Password = "not-a-real-password",
                Register = true,
            });
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private void AddExtension(string number, string forwarding = "") =>
            this.extensions.Insert(new Extension
            {
                Number = number,
                Name = "Phone " + number,
                Secret = "AAAAbbbbCCCCdddd1111",
                VoicemailEnabled = true,
                VoicemailPin = "4321",
                Forwarding = forwarding,
            });

        private long AddPhone(string mac, params PhoneButton[] keys)
        {
            var phoneID = this.phones.Register(mac, "VVX_410", "5.9.5.0614", "10.8.20.31").PhoneID;
            this.buttons.Replace(phoneID, keys);
            return phoneID;
        }

        private static PhoneButton Key(int position, string type, string value) =>
            new() { Position = position, TargetType = type, TargetValue = value };

        /// <summary>
        /// One of everything that can hold a destination, every destination slot on it pointed at
        /// <paramref name="target"/>: an inbound route, an IVR's key and its final destination, a
        /// time condition's three cases and a holiday of its own, a ring group's failover, and both
        /// ways of a call flow control. <paramref name="tag"/> keeps names and numbers apart when
        /// it is called twice.
        /// </summary>
        private void PointEverythingAt(Destination target, string tag)
        {
            var type = target.Type.ToString();
            var value = target.Value;

            this.inbound.Insert(new InboundRoute
            {
                TrunkID = this.trunkID,
                DID = "1777123456" + tag,
                DestinationType = type,
                DestinationValue = value,
            });

            var menu = new Ivr
            {
                Name = "Pointer menu " + tag,
                AnnouncementID = this.greetingID,
                DestinationType = type,
                DestinationValue = value,
            };
            menu.Entries.Add(new IvrEntry { Digit = "1", DestinationType = type, DestinationValue = value });
            this.ivrs.Insert(menu);

            this.conditions.Insert(new TimeCondition
            {
                Name = "Pointer hours " + tag,
                OpenDestinationType = type,
                OpenDestinationValue = value,
                ClosedDestinationType = type,
                ClosedDestinationValue = value,
                HolidayDestinationType = type,
                HolidayDestinationValue = value,
                Rules = new List<TimeConditionRule>
                {
                    new()
                    {
                        Kind = TimeConditionRuleKind.Holiday,
                        HolidayDate = "2026-12-25",
                        DestinationType = type,
                        DestinationValue = value,
                    },
                },
            });

            this.groups.Insert(new RingGroup
            {
                Number = "80" + tag,
                Name = "Pointer group " + tag,
                Members = "1001",
                DestinationType = type,
                DestinationValue = value,
            });

            this.controls.Insert(new CallFlowControl
            {
                Name = "Pointer switch " + tag,
                FeatureCode = "*7" + tag,
                NormalDestinationType = type,
                NormalDestinationValue = value,
                OverrideDestinationType = type,
                OverrideDestinationValue = value,
            });
        }

        /// <summary>How many stored destinations are exactly this one, across every table.</summary>
        private int References(Destination destination)
        {
            using var connection = this.database.Open();

            return DestinationColumns.Sum(c => connection.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM {c.Table} WHERE {c.TypeColumn} = @type AND {c.ValueColumn} = @value",
                new { type = destination.Type.ToString(), value = destination.Value }));
        }

        private List<string> TargetValues(long phoneID) =>
            this.buttons.GetForPhone(phoneID).Select(b => b.Key).ToList();

        [Fact]
        public void Renumbering_an_ivr_rewrites_every_destination_that_points_at_it()
        {
            var target = new Ivr { Name = "Main menu", AnnouncementID = this.greetingID, PlayExtension = "500" };
            this.ivrs.Insert(target);
            this.PointEverythingAt(new Destination(DestinationType.Ivr, "500"), "1");

            this.marker.Clear();

            var loaded = this.ivrs.GetByID(target.IvrID)!;
            loaded.PlayExtension = "510";
            this.ivrs.Update(loaded);

            Assert.Equal(0, this.References(new Destination(DestinationType.Ivr, "500")));
            Assert.Equal(EveryColumn, this.References(new Destination(DestinationType.Ivr, "510")));
            Assert.True(this.marker.IsPending);
        }

        /// <summary>
        /// "Press 9 to hear this again" points at the menu's own number, and is written from the
        /// object being saved rather than swept — so it has to follow the new number too.
        /// </summary>
        [Fact]
        public void Renumbering_an_ivr_carries_its_own_repeat_key_with_it()
        {
            var target = new Ivr { Name = "Main menu", AnnouncementID = this.greetingID, PlayExtension = "500" };
            target.Entries.Add(new IvrEntry { Digit = "9", DestinationType = "Ivr", DestinationValue = "500" });
            this.ivrs.Insert(target);

            var loaded = this.ivrs.GetByID(target.IvrID)!;
            loaded.PlayExtension = "510";
            this.ivrs.Update(loaded);

            Assert.Equal("Ivr:510", Assert.Single(this.ivrs.GetByID(target.IvrID)!.Entries).DestinationKey());
        }

        /// <summary>
        /// Renumbering a time condition is the same deal as an IVR: every stored destination that
        /// points at the old play extension follows it, and nothing changes until Apply.
        /// </summary>
        [Fact]
        public void Renumbering_a_time_condition_rewrites_every_destination_that_points_at_it()
        {
            var target = new TimeCondition
            {
                Name = "Office hours",
                PlayExtension = "600",
                Rules = new List<TimeConditionRule>
                {
                    new()
                    {
                        Kind = TimeConditionRuleKind.Weekly,
                        DaysMask = TimeConditionRule.Monday,
                        StartTime = "09:00",
                        EndTime = "17:00",
                    },
                },
            };
            this.conditions.Insert(target);
            this.PointEverythingAt(new Destination(DestinationType.TimeCondition, "600"), "2");

            this.marker.Clear();

            var loaded = this.conditions.GetByID(target.TimeConditionID)!;
            loaded.PlayExtension = "610";
            this.conditions.Update(loaded);

            Assert.Equal(0, this.References(new Destination(DestinationType.TimeCondition, "600")));
            Assert.Equal(EveryColumn, this.References(new Destination(DestinationType.TimeCondition, "610")));
            Assert.True(this.marker.IsPending);
        }

        /// <summary>
        /// A name another IVR already has is caught by the row's own UNIQUE, after the references
        /// have been rewritten — the one IVR refusal that happens mid-way, so it is the one that
        /// proves the transaction.
        /// </summary>
        [Fact]
        public void An_ivr_renumber_refused_mid_way_rewrites_nothing()
        {
            var target = new Ivr { Name = "Main menu", AnnouncementID = this.greetingID, PlayExtension = "500" };
            this.ivrs.Insert(target);
            this.PointEverythingAt(new Destination(DestinationType.Ivr, "500"), "1");

            var loaded = this.ivrs.GetByID(target.IvrID)!;
            loaded.PlayExtension = "510";
            loaded.Name = "Pointer menu 1";

            Assert.Throws<ValidationFailedException>(() => this.ivrs.Update(loaded));

            Assert.Equal(EveryColumn, this.References(new Destination(DestinationType.Ivr, "500")));
            Assert.Equal(0, this.References(new Destination(DestinationType.Ivr, "510")));
            Assert.Equal("500", this.ivrs.GetByID(target.IvrID)!.PlayExtension);
        }

        [Fact]
        public void An_ivr_renumber_onto_a_taken_number_rewrites_nothing()
        {
            var target = new Ivr { Name = "Main menu", AnnouncementID = this.greetingID, PlayExtension = "500" };
            this.ivrs.Insert(target);
            this.PointEverythingAt(new Destination(DestinationType.Ivr, "500"), "1");

            var loaded = this.ivrs.GetByID(target.IvrID)!;
            loaded.PlayExtension = "1001";

            Assert.Throws<ValidationFailedException>(() => this.ivrs.Update(loaded));

            Assert.Equal(EveryColumn, this.References(new Destination(DestinationType.Ivr, "500")));
        }

        /// <summary>
        /// Clearing the play extension takes the menu out of the dialplan, which to a reference is
        /// a delete, and deletes are not cascaded (D35). The references are left for the picker to
        /// show as gone; nothing is rewritten into a destination with no number.
        /// </summary>
        [Fact]
        public void Clearing_an_ivrs_number_leaves_the_references_alone()
        {
            var target = new Ivr { Name = "Main menu", AnnouncementID = this.greetingID, PlayExtension = "500" };
            this.ivrs.Insert(target);

            this.inbound.Insert(new InboundRoute
            {
                TrunkID = this.trunkID,
                DID = "17771234567",
                DestinationType = "Ivr",
                DestinationValue = "500",
            });

            var loaded = this.ivrs.GetByID(target.IvrID)!;
            loaded.PlayExtension = "";
            this.ivrs.Update(loaded);

            Assert.Equal(1, this.References(new Destination(DestinationType.Ivr, "500")));
        }

        [Fact]
        public void Renumbering_a_ring_group_rewrites_every_destination_that_points_at_it()
        {
            var target = new RingGroup { Number = "600", Name = "Sales", Members = "1001,1002" };
            this.groups.Insert(target);
            this.PointEverythingAt(new Destination(DestinationType.RingGroup, "600"), "1");

            this.marker.Clear();

            var loaded = this.groups.GetByID(target.RingGroupID)!;
            loaded.Number = "610";
            this.groups.Update(loaded);

            Assert.Equal(0, this.References(new Destination(DestinationType.RingGroup, "600")));
            Assert.Equal(EveryColumn, this.References(new Destination(DestinationType.RingGroup, "610")));
            Assert.True(this.marker.IsPending);
        }

        /// <summary>
        /// Another group's number is only caught by the UNIQUE on the row, after the sweep has run,
        /// so this is a refusal genuinely mid-way through.
        /// </summary>
        [Fact]
        public void A_ring_group_renumber_refused_mid_way_rewrites_nothing()
        {
            var target = new RingGroup { Number = "600", Name = "Sales", Members = "1001" };
            this.groups.Insert(target);
            this.groups.Insert(new RingGroup { Number = "620", Name = "Support", Members = "1002" });
            this.PointEverythingAt(new Destination(DestinationType.RingGroup, "600"), "1");

            var loaded = this.groups.GetByID(target.RingGroupID)!;
            loaded.Number = "620";

            Assert.Throws<ValidationFailedException>(() => this.groups.Update(loaded));

            Assert.Equal(EveryColumn, this.References(new Destination(DestinationType.RingGroup, "600")));
            Assert.Equal(0, this.References(new Destination(DestinationType.RingGroup, "620")));
            Assert.Equal("600", this.groups.GetByID(target.RingGroupID)!.Number);
        }

        /// <summary>
        /// An extension is keyed by its number twice over — the phone and the mailbox — and is also
        /// named by forwarding lists (D130), ring group members (D53) and phone keys, both lamps
        /// and lines (D121). All of it moves.
        /// </summary>
        [Fact]
        public void Renumbering_an_extension_rewrites_destinations_forwarding_members_and_keys()
        {
            this.AddExtension("1003", forwarding: "1002 7146085242");
            this.PointEverythingAt(new Destination(DestinationType.Extension, "1002"), "1");
            this.PointEverythingAt(new Destination(DestinationType.Voicemail, "1002"), "2");

            var group = new RingGroup { Number = "600", Name = "Sales", Members = "1001,1002,1003" };
            this.groups.Insert(group);

            var watcher = this.AddPhone("0004f2aabb01", Key(1, PhoneButtonTarget.Line, "1001"), Key(2, PhoneButtonTarget.Blf, "1002"));
            var owner = this.AddPhone("0004f2aabb02", Key(1, PhoneButtonTarget.Line, "1002"), Key(2, PhoneButtonTarget.Blf, "1001"));

            this.marker.Clear();

            var loaded = this.extensions.GetByNumber("1002")!;
            loaded.Number = "1005";
            this.extensions.Update(loaded);

            Assert.Equal(0, this.References(new Destination(DestinationType.Extension, "1002")));
            Assert.Equal(0, this.References(new Destination(DestinationType.Voicemail, "1002")));
            Assert.Equal(EveryColumn, this.References(new Destination(DestinationType.Extension, "1005")));
            Assert.Equal(EveryColumn, this.References(new Destination(DestinationType.Voicemail, "1005")));

            Assert.Equal("1005 7146085242", this.extensions.GetByNumber("1003")!.Forwarding);
            Assert.Equal("1001,1005,1003", this.groups.GetByID(group.RingGroupID)!.Members);
            Assert.Equal(new[] { "Line:1001", "Blf:1005" }, this.TargetValues(watcher));
            Assert.Equal(new[] { "Line:1005", "Blf:1001" }, this.TargetValues(owner));
            Assert.True(this.marker.IsPending);
        }

        /// <summary>
        /// "Keep my own handset ringing" is the extension's own number in its own forwarding list,
        /// written from the object in hand rather than swept — it has to follow the new number, not
        /// start ringing whoever is given the old one.
        /// </summary>
        [Fact]
        public void Renumbering_an_extension_carries_its_own_forwarding_with_it()
        {
            this.AddExtension("1003", forwarding: "1003 7146085242");

            var loaded = this.extensions.GetByNumber("1003")!;
            loaded.Number = "1005";
            this.extensions.Update(loaded);

            Assert.Equal("1005 7146085242", this.extensions.GetByNumber("1005")!.Forwarding);
        }

        /// <summary>
        /// A token is rewritten only when it is the whole number: 1002555 in a list is an outside
        /// number, not extension 1002.
        /// </summary>
        [Fact]
        public void Only_whole_tokens_are_rewritten()
        {
            this.AddExtension("1003", forwarding: "1002555 1002");

            var loaded = this.extensions.GetByNumber("1002")!;
            loaded.Number = "1005";
            this.extensions.Update(loaded);

            Assert.Equal("1002555 1005", this.extensions.GetByNumber("1003")!.Forwarding);
        }

        [Fact]
        public void An_extension_renumber_refused_mid_way_rewrites_nothing()
        {
            this.AddExtension("1003", forwarding: "1002 7146085242");
            this.PointEverythingAt(new Destination(DestinationType.Extension, "1002"), "1");

            var group = new RingGroup { Number = "600", Name = "Sales", Members = "1002,1003" };
            this.groups.Insert(group);

            var owner = this.AddPhone("0004f2aabb02", Key(1, PhoneButtonTarget.Line, "1002"));

            // 1001 is taken, and only the row's own UNIQUE says so — after the sweep has run.
            var loaded = this.extensions.GetByNumber("1002")!;
            loaded.Number = "1001";

            Assert.Throws<ValidationFailedException>(() => this.extensions.Update(loaded));

            Assert.Equal(EveryColumn, this.References(new Destination(DestinationType.Extension, "1002")));
            Assert.Equal("1002 7146085242", this.extensions.GetByNumber("1003")!.Forwarding);
            Assert.Equal("1002,1003", this.groups.GetByID(group.RingGroupID)!.Members);
            Assert.Equal(new[] { "Line:1002" }, this.TargetValues(owner));
            Assert.NotNull(this.extensions.GetByNumber("1002"));
        }

        [Fact]
        public void Changing_a_call_flow_controls_code_rewrites_destinations_and_keys()
        {
            var target = new CallFlowControl { Name = "Night mode", FeatureCode = "*28" };
            this.controls.Insert(target);
            this.PointEverythingAt(new Destination(DestinationType.CallFlowControl, "*28"), "1");

            var phone = this.AddPhone("0004f2aabb01", Key(1, PhoneButtonTarget.Line, "1001"), Key(2, PhoneButtonTarget.CallFlowControl, "*28"));

            this.marker.Clear();

            var loaded = this.controls.GetByID(target.CallFlowControlID)!;
            loaded.FeatureCode = "*55";
            this.controls.Update(loaded);

            Assert.Equal(0, this.References(new Destination(DestinationType.CallFlowControl, "*28")));
            Assert.Equal(EveryColumn, this.References(new Destination(DestinationType.CallFlowControl, "*55")));
            Assert.Equal(new[] { "Line:1001", "CallFlowControl:*55" }, this.TargetValues(phone));
            Assert.True(this.marker.IsPending);
        }

        [Fact]
        public void A_call_flow_control_renumber_onto_a_taken_code_rewrites_nothing()
        {
            var target = new CallFlowControl { Name = "Night mode", FeatureCode = "*28" };
            this.controls.Insert(target);
            this.controls.Insert(new CallFlowControl { Name = "Lunch", FeatureCode = "*56" });
            this.PointEverythingAt(new Destination(DestinationType.CallFlowControl, "*28"), "1");

            var phone = this.AddPhone("0004f2aabb01", Key(1, PhoneButtonTarget.Line, "1001"), Key(2, PhoneButtonTarget.CallFlowControl, "*28"));

            var loaded = this.controls.GetByID(target.CallFlowControlID)!;
            loaded.FeatureCode = "*56";

            Assert.Throws<ValidationFailedException>(() => this.controls.Update(loaded));

            Assert.Equal(EveryColumn, this.References(new Destination(DestinationType.CallFlowControl, "*28")));
            Assert.Equal(new[] { "Line:1001", "CallFlowControl:*28" }, this.TargetValues(phone));
        }

        /// <summary>Saving without changing the number touches no other row.</summary>
        [Fact]
        public void A_save_that_keeps_the_number_rewrites_nothing()
        {
            var target = new RingGroup { Number = "600", Name = "Sales", Members = "1001" };
            this.groups.Insert(target);
            this.PointEverythingAt(new Destination(DestinationType.RingGroup, "600"), "1");

            var loaded = this.groups.GetByID(target.RingGroupID)!;
            loaded.Name = "Sales team";
            this.groups.Update(loaded);

            Assert.Equal(EveryColumn, this.References(new Destination(DestinationType.RingGroup, "600")));
        }

        [Fact]
        public void The_sweep_refuses_to_change_a_destinations_kind()
        {
            using var connection = this.database.Open();
            using var transaction = connection.BeginTransaction();

            Assert.Throws<InvalidOperationException>(() => Renumbering.Destinations(connection, transaction,
                new Destination(DestinationType.Ivr, "500"), new Destination(DestinationType.RingGroup, "500")));
        }

        [Fact]
        public void The_sweep_refuses_to_write_a_destination_that_does_not_validate()
        {
            using var connection = this.database.Open();
            using var transaction = connection.BeginTransaction();

            Assert.Throws<InvalidOperationException>(() => Renumbering.Destinations(connection, transaction,
                new Destination(DestinationType.Ivr, "500"), new Destination(DestinationType.Ivr, "")));
        }
    }
}
