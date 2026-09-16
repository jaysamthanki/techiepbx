using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Ring group validation, including the halves that need the database: the number has to be
    /// free, the members have to exist, and a group must not send unanswered calls round in a
    /// circle (D52, D54).
    /// </summary>
    public class RingGroupRepositoryTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-groups-").FullName;
        private readonly Database database;
        private readonly ExtensionRepository extensions;
        private readonly RingGroupRepository groups;

        public RingGroupRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.extensions = new ExtensionRepository(this.database);
            this.groups = new RingGroupRepository(this.database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private void AddExtension(string number, bool enabled = true) =>
            this.extensions.Insert(new Extension
            {
                Number = number,
                Name = "Phone " + number,
                Secret = "AAAAbbbbCCCCdddd1111",
                Enabled = enabled,
            });

        private static RingGroup Group(string number = "600", string members = "1001") => new()
        {
            Number = number,
            Name = "Support",
            Members = members,
            Strategy = "All",
            RingSeconds = 20,
            DestinationType = "Hangup",
        };

        [Fact]
        public void Insert_then_read_back()
        {
            AddExtension("1001");
            AddExtension("1002");

            var id = this.groups.Insert(new RingGroup
            {
                Number = "600",
                Name = "Support",
                Strategy = "Hunt",
                Members = "1002,1001",
                RingSeconds = 15,
                CallerIDPrefix = "Support: ",
                DestinationType = "Extension",
                DestinationValue = "1002",
            });

            var loaded = this.groups.GetByID(id)!;

            Assert.Equal("600", loaded.Number);
            Assert.Equal(RingStrategy.Hunt, loaded.ToStrategy());
            Assert.Equal(new[] { "1002", "1001" }, loaded.MemberList());
            Assert.Equal(15, loaded.RingSeconds);
            Assert.Equal("Support: ", loaded.CallerIDPrefix);
            Assert.Equal("Extension:1002", loaded.ToDestination().Key);
        }

        /// <summary>The member order is what Hunt follows, so it has to survive storage (D53).</summary>
        [Fact]
        public void The_member_order_is_kept()
        {
            AddExtension("1001");
            AddExtension("1002");
            AddExtension("1003");

            var id = this.groups.Insert(Group(members: "1003,1001,1002"));

            Assert.Equal(new[] { "1003", "1001", "1002" }, this.groups.GetByID(id)!.MemberList());
        }

        [Fact]
        public void A_number_an_extension_already_uses_is_refused()
        {
            AddExtension("1001");

            var ex = Assert.Throws<ValidationFailedException>(() => this.groups.Insert(Group("1001")));

            Assert.Contains("already uses that number", ex.Message);
        }

        [Fact]
        public void Two_groups_cannot_share_a_number()
        {
            AddExtension("1001");
            this.groups.Insert(Group());

            var ex = Assert.Throws<ValidationFailedException>(() => this.groups.Insert(Group()));

            Assert.Contains("already exists", ex.Message);
        }

        [Fact]
        public void A_member_that_does_not_exist_is_refused()
        {
            var ex = Assert.Throws<ValidationFailedException>(() => this.groups.Insert(Group()));

            Assert.Contains("no extension 1001", ex.Message);
        }

        [Fact]
        public void A_disabled_member_is_refused()
        {
            AddExtension("1001", enabled: false);

            var ex = Assert.Throws<ValidationFailedException>(() => this.groups.Insert(Group()));

            Assert.Contains("disabled", ex.Message);
        }

        [Fact]
        public void A_group_needs_at_least_one_member()
        {
            var ex = Assert.Throws<ValidationFailedException>(() => this.groups.Insert(Group(members: "")));

            Assert.Contains("at least one member", ex.Message);
        }

        [Fact]
        public void The_same_member_twice_is_refused()
        {
            AddExtension("1001");

            var ex = Assert.Throws<ValidationFailedException>(() => this.groups.Insert(Group(members: "1001,1001")));

            Assert.Contains("only be in the group once", ex.Message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(301)]
        public void A_ring_time_outside_the_range_is_refused(int seconds)
        {
            AddExtension("1001");
            var group = Group();
            group.RingSeconds = seconds;

            Assert.Throws<ValidationFailedException>(() => this.groups.Insert(group));
        }

        [Fact]
        public void A_strategy_this_system_does_not_have_is_refused()
        {
            AddExtension("1001");
            var group = Group();
            group.Strategy = "Queue";

            var ex = Assert.Throws<ValidationFailedException>(() => this.groups.Insert(group));

            Assert.Contains("ringing strategy", ex.Message);
        }

        [Fact]
        public void A_destination_that_is_gone_is_refused()
        {
            AddExtension("1001");
            var group = Group();
            group.DestinationType = "Extension";
            group.DestinationValue = "9999";

            var ex = Assert.Throws<ValidationFailedException>(() => this.groups.Insert(group));

            Assert.Contains("not there any more", ex.Message);
        }

        /// <summary>A call that rings for ever is worse than one that is refused up front (D54).</summary>
        [Fact]
        public void A_group_that_sends_unanswered_calls_to_itself_is_refused()
        {
            AddExtension("1001");
            var group = Group();
            group.DestinationType = "RingGroup";
            group.DestinationValue = "600";

            var ex = Assert.Throws<ValidationFailedException>(() => this.groups.Insert(group));

            Assert.Contains("itself", ex.Message);
        }

        [Fact]
        public void A_ring_round_two_groups_is_refused()
        {
            AddExtension("1001");
            this.groups.Insert(Group("600"));

            var second = Group("601");
            second.DestinationType = "RingGroup";
            second.DestinationValue = "600";
            this.groups.Insert(second);

            // 600 -> 601 -> 600 would be a call nobody can end.
            var first = this.groups.GetByNumber("600")!;
            first.DestinationType = "RingGroup";
            first.DestinationValue = "601";

            var ex = Assert.Throws<ValidationFailedException>(() => this.groups.Update(first));

            Assert.Contains("comes back round", ex.Message);
        }

        [Fact]
        public void A_chain_that_ends_somewhere_is_fine()
        {
            AddExtension("1001");
            this.groups.Insert(Group("600"));

            var second = Group("601");
            second.DestinationType = "RingGroup";
            second.DestinationValue = "600";

            Assert.Empty(new List<string>());
            this.groups.Insert(second);
            Assert.Equal(2, this.groups.GetAll().Count);
        }

        [Fact]
        public void Update_and_delete()
        {
            AddExtension("1001");
            var group = Group();
            this.groups.Insert(group);

            group.Name = "Renamed";
            group.Enabled = false;
            this.groups.Update(group);

            var loaded = this.groups.GetByID(group.RingGroupID)!;
            Assert.Equal("Renamed", loaded.Name);
            Assert.False(loaded.Enabled);

            this.groups.Delete(group.RingGroupID);
            Assert.Null(this.groups.GetByID(group.RingGroupID));
        }

        [Fact]
        public void Every_write_raises_the_apply_marker()
        {
            AddExtension("1001");
            var marker = new ConfigPendingMarker(this.database);
            marker.Clear();

            var id = this.groups.Insert(Group());
            Assert.True(marker.IsPending);

            marker.Clear();
            this.groups.Delete(id);
            Assert.True(marker.IsPending);
        }
    }
}
