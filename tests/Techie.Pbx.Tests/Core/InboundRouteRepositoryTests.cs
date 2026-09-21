using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Inbound route validation, including the halves that need the database: the trunk has to be
    /// there, the destination has to still exist (D35), and a trunk gets one catch-all (D49).
    /// </summary>
    public class InboundRouteRepositoryTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-inbound-").FullName;
        private readonly Database database;
        private readonly ExtensionRepository extensions;
        private readonly InboundRouteRepository inbound;
        private readonly TrunkRepository trunks;

        public InboundRouteRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.extensions = new ExtensionRepository(this.database);
            this.inbound = new InboundRouteRepository(this.database);
            this.trunks = new TrunkRepository(this.database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private long AddExtension(string number = "1001") =>
            this.extensions.Insert(new Extension { Number = number, Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" });

        private long AddTrunk(string name = "callcentric", bool enabled = true) =>
            this.trunks.Insert(new Trunk
            {
                Name = name,
                ServerHost = "callcentric.com",
                Username = "17771234567",
                Password = "not-a-real-password",
                Register = true,
                Enabled = enabled,
            });

        private static InboundRoute Route(long trunkID, string did = "17771234567") => new()
        {
            TrunkID = trunkID,
            DID = did,
            DestinationType = "Extension",
            DestinationValue = "1001",
            Description = "Main line",
        };

        [Fact]
        public void Insert_then_read_back()
        {
            AddExtension();
            var trunkID = AddTrunk();
            var id = this.inbound.Insert(Route(trunkID));

            var loaded = this.inbound.GetByID(id)!;

            Assert.Equal("17771234567", loaded.DID);
            Assert.Equal("Extension", loaded.DestinationType);
            Assert.Equal("1001", loaded.DestinationValue);
            Assert.Equal("Main line", loaded.Description);
            Assert.False(loaded.CatchAll);
            Assert.True(loaded.Enabled);
            Assert.Equal("Extension:1001", loaded.ToDestination().Key);
        }

        [Fact]
        public void A_route_to_a_destination_that_is_gone_is_refused()
        {
            var trunkID = AddTrunk();

            // No extensions at all, so Extension:1001 points at nothing.
            var ex = Assert.Throws<ValidationFailedException>(() => this.inbound.Insert(Route(trunkID)));

            Assert.Contains("destination", ex.Message);
        }

        [Fact]
        public void A_route_for_a_trunk_that_does_not_exist_is_refused()
        {
            AddExtension();

            var ex = Assert.Throws<ValidationFailedException>(() => this.inbound.Insert(Route(999)));

            Assert.Contains("trunk", ex.Message);
        }

        [Fact]
        public void A_route_for_a_disabled_trunk_is_refused()
        {
            AddExtension();
            var trunkID = AddTrunk(enabled: false);

            var ex = Assert.Throws<ValidationFailedException>(() => this.inbound.Insert(Route(trunkID)));

            Assert.Contains("disabled", ex.Message);
        }

        [Fact]
        public void Two_routes_cannot_claim_the_same_number_on_one_trunk()
        {
            AddExtension();
            var trunkID = AddTrunk();
            this.inbound.Insert(Route(trunkID));

            var ex = Assert.Throws<ValidationFailedException>(() => this.inbound.Insert(Route(trunkID)));

            Assert.Contains("already a route for 17771234567", ex.Message);
        }

        [Fact]
        public void The_same_number_on_a_different_trunk_is_fine()
        {
            AddExtension();
            var first = AddTrunk();
            var second = AddTrunk("other");

            this.inbound.Insert(Route(first));
            this.inbound.Insert(Route(second));

            Assert.Equal(2, this.inbound.GetAll().Count);
        }

        /// <summary>A second catch-all would be a second _X. in one context (D49).</summary>
        [Fact]
        public void A_trunk_gets_one_catch_all()
        {
            var trunkID = AddTrunk();
            var catchAll = new InboundRoute { TrunkID = trunkID, CatchAll = true, DestinationType = "Hangup" };
            this.inbound.Insert(catchAll);

            var second = new InboundRoute { TrunkID = trunkID, CatchAll = true, DestinationType = "Hangup" };
            var ex = Assert.Throws<ValidationFailedException>(() => this.inbound.Insert(second));

            Assert.Contains("catch-all", ex.Message);
        }

        [Fact]
        public void A_catch_all_with_a_did_of_its_own_is_refused()
        {
            var trunkID = AddTrunk();
            var route = new InboundRoute { TrunkID = trunkID, CatchAll = true, DID = "17771234567", DestinationType = "Hangup" };

            var ex = Assert.Throws<ValidationFailedException>(() => this.inbound.Insert(route));

            Assert.Contains("no DID of its own", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-digits")]
        [InlineData("1777 123 4567")]
        [InlineData("+17771234567")]
        [InlineData("1234567890123456")]
        public void A_did_that_is_not_digits_is_refused(string did)
        {
            AddExtension();
            var trunkID = AddTrunk();

            var ex = Assert.Throws<ValidationFailedException>(() => this.inbound.Insert(Route(trunkID, did)));

            Assert.Contains("DID", ex.Message);
        }

        [Fact]
        public void Routes_come_back_with_each_trunks_catch_all_last()
        {
            AddExtension();
            var trunkID = AddTrunk();
            this.inbound.Insert(new InboundRoute { TrunkID = trunkID, CatchAll = true, DestinationType = "Hangup" });
            this.inbound.Insert(Route(trunkID, "17771234568"));
            this.inbound.Insert(Route(trunkID, "17771234567"));

            Assert.Equal(
                new[] { "17771234567", "17771234568", "" },
                this.inbound.GetAll().Select(r => r.DID));
        }

        [Fact]
        public void Update_and_delete()
        {
            AddExtension();
            var trunkID = AddTrunk();
            var route = Route(trunkID);
            this.inbound.Insert(route);

            route.DestinationType = "Hangup";
            route.DestinationValue = "";
            route.Enabled = false;
            this.inbound.Update(route);

            var loaded = this.inbound.GetByID(route.InboundRouteID)!;
            Assert.Equal("Hangup", loaded.DestinationType);
            Assert.False(loaded.Enabled);

            this.inbound.Delete(route.InboundRouteID);
            Assert.Null(this.inbound.GetByID(route.InboundRouteID));
        }

        [Fact]
        public void A_trunk_an_inbound_route_still_uses_cannot_be_deleted()
        {
            AddExtension();
            var trunkID = AddTrunk();
            this.inbound.Insert(Route(trunkID));

            Assert.Throws<ValidationFailedException>(() => this.trunks.Delete(trunkID));
            Assert.NotNull(this.trunks.GetByID(trunkID));
        }

        /// <summary>
        /// The music a caller on this route hears while they are held (D122 amended). Stored as the
        /// class's ID, so renaming the class renames what the dialplan says at the next apply.
        /// </summary>
        [Fact]
        public void A_route_can_name_a_music_on_hold_class()
        {
            AddExtension();
            var trunkID = AddTrunk();
            var mohClasses = new MohClassRepository(this.database);
            var mohClassID = mohClasses.Insert(new MohClass { Name = "Front Desk", Directory = "front-desk" });

            var route = Route(trunkID);
            route.MohClassID = mohClassID;
            this.inbound.Insert(route);

            Assert.Equal(mohClassID, this.inbound.GetByID(route.InboundRouteID)!.MohClassID);
        }

        /// <summary>
        /// The renderer writes the class's name into the dialplan and refuses one it cannot find,
        /// so a route must not be able to name a class that is not there in the first place.
        /// </summary>
        [Fact]
        public void A_route_cannot_name_a_music_on_hold_class_that_is_not_there()
        {
            AddExtension();
            var trunkID = AddTrunk();

            var route = Route(trunkID);
            route.MohClassID = 999;

            var ex = Assert.Throws<ValidationFailedException>(() => this.inbound.Insert(route));
            Assert.Contains("music on hold class is not there", ex.Message);
        }

        /// <summary>
        /// Deleting a class does not delete the routes that played it: they go back to naming no
        /// class, which is the same silence-or-whatever-Asterisk-falls-back-to every route had
        /// before the column existed. A cascade here would delete an inbound route — and with it
        /// the number a site advertises — because somebody tidied up the hold music.
        /// </summary>
        [Fact]
        public void Deleting_a_class_puts_the_routes_that_played_it_back_to_none()
        {
            AddExtension();
            var trunkID = AddTrunk();
            var mohClasses = new MohClassRepository(this.database);
            var mohClassID = mohClasses.Insert(new MohClass { Name = "Front Desk", Directory = "front-desk" });

            var route = Route(trunkID);
            route.MohClassID = mohClassID;
            this.inbound.Insert(route);

            mohClasses.Delete(mohClassID);

            var loaded = this.inbound.GetByID(route.InboundRouteID);

            Assert.NotNull(loaded);
            Assert.Null(loaded!.MohClassID);
        }

        [Fact]
        public void Every_write_raises_the_apply_marker()
        {
            AddExtension();
            var trunkID = AddTrunk();
            var marker = new ConfigPendingMarker(this.database);
            marker.Clear();

            var id = this.inbound.Insert(Route(trunkID));
            Assert.True(marker.IsPending);

            marker.Clear();
            this.inbound.Delete(id);
            Assert.True(marker.IsPending);
        }
    }
}
