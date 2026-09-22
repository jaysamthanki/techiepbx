using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The half of route validation that needs the database: the trunk a route points at has to
    /// be there and switched on, and a trunk cannot be deleted out from under a route.
    /// </summary>
    public class OutboundRouteRepositoryTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-routes-").FullName;
        private readonly Database database;
        private readonly OutboundRouteRepository routes;
        private readonly TrunkRepository trunks;

        public OutboundRouteRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.routes = new OutboundRouteRepository(this.database);
            this.trunks = new TrunkRepository(this.database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

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

        private static OutboundRoute Route(long trunkID, string name = "long-distance") => new()
        {
            Name = name,
            DialPattern = "_1NXXXXXXXXX",
            TrunkID = trunkID,
            Priority = 10,
        };

        [Fact]
        public void Insert_then_read_back()
        {
            var trunkID = AddTrunk();
            var id = this.routes.Insert(Route(trunkID));

            var loaded = this.routes.GetByID(id)!;

            Assert.Equal("long-distance", loaded.Name);
            Assert.Equal("_1NXXXXXXXXX", loaded.DialPattern);
            Assert.Equal(trunkID, loaded.TrunkID);
            Assert.Equal(10, loaded.Priority);
            Assert.True(loaded.Enabled);
        }

        [Fact]
        public void Routes_come_back_in_the_order_they_are_tried()
        {
            var trunkID = AddTrunk();
            this.routes.Insert(new OutboundRoute { Name = "zebra", DialPattern = "_2XXXXXX", TrunkID = trunkID, Priority = 10 });
            this.routes.Insert(new OutboundRoute { Name = "first", DialPattern = "_3XXXXXX", TrunkID = trunkID, Priority = 1 });
            this.routes.Insert(new OutboundRoute { Name = "apple", DialPattern = "_4XXXXXX", TrunkID = trunkID, Priority = 10 });

            Assert.Equal(new[] { "first", "apple", "zebra" }, this.routes.GetAll().Select(r => r.Name));
        }

        [Fact]
        public void A_route_to_a_trunk_that_does_not_exist_is_refused()
        {
            var ex = Assert.Throws<ValidationFailedException>(() => this.routes.Insert(Route(999)));

            Assert.Contains("trunk", ex.Message);
        }

        [Fact]
        public void A_route_to_a_disabled_trunk_is_refused()
        {
            var trunkID = AddTrunk(enabled: false);

            var ex = Assert.Throws<ValidationFailedException>(() => this.routes.Insert(Route(trunkID)));

            Assert.Contains("disabled", ex.Message);
        }

        /// <summary>The guard is in the repository, so no route can be stored that reaches it.</summary>
        [Fact]
        public void An_international_route_cannot_be_stored()
        {
            var trunkID = AddTrunk();
            var route = Route(trunkID);
            route.DialPattern = "_011.";

            var ex = Assert.Throws<ValidationFailedException>(() => this.routes.Insert(route));

            Assert.Contains("international", ex.Message);
            Assert.Empty(this.routes.GetAll());
        }

        [Fact]
        public void Two_routes_cannot_share_a_name()
        {
            var trunkID = AddTrunk();
            this.routes.Insert(Route(trunkID));

            var ex = Assert.Throws<ValidationFailedException>(() => this.routes.Insert(Route(trunkID)));

            Assert.Contains("already exists", ex.Message);
        }

        /// <summary>
        /// Deleting the trunk would leave a route dialling nothing. Saying so beats deleting the
        /// route as well without being asked.
        /// </summary>
        [Fact]
        public void A_trunk_a_route_still_uses_cannot_be_deleted()
        {
            var trunkID = AddTrunk();
            this.routes.Insert(Route(trunkID));

            var ex = Assert.Throws<ValidationFailedException>(() => this.trunks.Delete(trunkID));

            Assert.Contains("outbound route", ex.Message);
            Assert.NotNull(this.trunks.GetByID(trunkID));
        }

        [Fact]
        public void A_trunk_is_deletable_once_its_routes_are_gone()
        {
            var trunkID = AddTrunk();
            var routeID = this.routes.Insert(Route(trunkID));

            this.routes.Delete(routeID);
            this.trunks.Delete(trunkID);

            Assert.Null(this.trunks.GetByID(trunkID));
        }

        [Fact]
        public void Update_and_delete()
        {
            var trunkID = AddTrunk();
            var route = Route(trunkID);
            this.routes.Insert(route);

            route.Priority = 50;
            route.Enabled = false;
            this.routes.Update(route);

            var loaded = this.routes.GetByID(route.OutboundRouteID)!;
            Assert.Equal(50, loaded.Priority);
            Assert.False(loaded.Enabled);

            this.routes.Delete(route.OutboundRouteID);
            Assert.Null(this.routes.GetByID(route.OutboundRouteID));
        }

        [Fact]
        public void Every_write_raises_the_apply_marker()
        {
            var marker = new ConfigPendingMarker(this.database);
            var trunkID = AddTrunk();
            marker.Clear();

            var id = this.routes.Insert(Route(trunkID));
            Assert.True(marker.IsPending);

            marker.Clear();
            this.routes.Delete(id);
            Assert.True(marker.IsPending);
        }

        /// <summary>
        /// A pattern typed without its underscore is stored with it (D109): the admin meant a
        /// pattern, and the repository keeps the promise the form's hint makes.
        /// </summary>
        [Fact]
        public void A_pattern_typed_without_the_underscore_is_stored_with_it()
        {
            var trunkID = AddTrunk();
            var route = Route(trunkID);
            route.DialPattern = "NXXXXXX";

            this.routes.Insert(route);

            Assert.Equal("_NXXXXXX", this.routes.GetByID(route.OutboundRouteID)!.DialPattern);
        }

        [Fact]
        public void Prepend_and_strip_digits_round_trip()
        {
            var trunkID = AddTrunk();
            var route = Route(trunkID);
            route.PrependDigits = "1714";
            route.StripDigits = 0;

            this.routes.Insert(route);

            var loaded = this.routes.GetByID(route.OutboundRouteID)!;
            Assert.Equal("1714", loaded.PrependDigits);
            Assert.Equal(0, loaded.StripDigits);

            loaded.PrependDigits = "1";
            loaded.StripDigits = 1;
            this.routes.Update(loaded);

            var updated = this.routes.GetByID(loaded.OutboundRouteID)!;
            Assert.Equal("1", updated.PrependDigits);
            Assert.Equal(1, updated.StripDigits);
        }

        /// <summary>
        /// The route's own caller ID (D125), in both the forms an admin might type it and empty for
        /// the route that names none — which is the default, and then the trunk says who we are.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("17141234567")]
        [InlineData("\"Acme Sales\" <17141234567>")]
        public void A_route_caller_id_round_trips(string callerID)
        {
            var trunkID = AddTrunk();
            var route = Route(trunkID);
            route.CallerID = callerID;

            this.routes.Insert(route);

            Assert.Equal(callerID, this.routes.GetByID(route.OutboundRouteID)!.CallerID);
        }

        /// <summary>A caller ID is trimmed on the way in, like the pattern and the name are.</summary>
        [Fact]
        public void A_route_caller_id_is_stored_without_stray_whitespace()
        {
            var trunkID = AddTrunk();
            var route = Route(trunkID);
            route.CallerID = "  17141234567  ";

            this.routes.Insert(route);

            Assert.Equal("17141234567", this.routes.GetByID(route.OutboundRouteID)!.CallerID);
        }

        /// <summary>
        /// What is refused is what could not be written into the dialplan, or could be written and
        /// mean something else: the caller ID ends up inside a <c>Set(CALLERID(all)=...)</c>, so a
        /// comma, a bracket or a quote of its own is not allowed, and a number is digits (D125).
        /// </summary>
        [Theory]
        [InlineData("+17141234567")]
        [InlineData("1 714 123 4567")]
        [InlineData("Acme Sales")]
        [InlineData("\"Acme, Sales\" <17141234567>")]
        [InlineData("\"Acme (Sales)\" <17141234567>")]
        public void A_route_caller_id_that_could_not_be_written_is_refused(string callerID)
        {
            var trunkID = AddTrunk();
            var route = Route(trunkID);
            route.CallerID = callerID;

            var ex = Assert.Throws<ValidationFailedException>(() => this.routes.Insert(route));

            Assert.Contains("Caller ID", ex.Message);
            Assert.Empty(this.routes.GetAll());
        }

        /// <summary>
        /// The class an outbound caller hears while the far side holds them (D125). Stored as a
        /// reference, like the inbound one, so this is the same three behaviours: it round trips, a
        /// class that is not there is refused, and deleting a class puts the route back to none
        /// rather than taking the route with it.
        /// </summary>
        [Fact]
        public void A_music_on_hold_class_round_trips()
        {
            var trunkID = AddTrunk();
            var mohClasses = new MohClassRepository(this.database);
            var mohClassID = mohClasses.Insert(new MohClass { Name = "Front Desk", Directory = "front-desk" });
            var route = Route(trunkID);
            route.MohClassID = mohClassID;

            this.routes.Insert(route);

            Assert.Equal(mohClassID, this.routes.GetByID(route.OutboundRouteID)!.MohClassID);
        }

        [Fact]
        public void A_music_on_hold_class_that_is_not_there_is_refused()
        {
            var trunkID = AddTrunk();
            var route = Route(trunkID);
            route.MohClassID = 999;

            var ex = Assert.Throws<ValidationFailedException>(() => this.routes.Insert(route));

            Assert.Contains("music on hold class", ex.Message);
            Assert.Empty(this.routes.GetAll());
        }

        [Fact]
        public void Deleting_a_class_leaves_the_route_with_no_class_named()
        {
            var trunkID = AddTrunk();
            var mohClasses = new MohClassRepository(this.database);
            var mohClassID = mohClasses.Insert(new MohClass { Name = "Front Desk", Directory = "front-desk" });
            var route = Route(trunkID);
            route.MohClassID = mohClassID;
            this.routes.Insert(route);

            mohClasses.Delete(mohClassID);

            var loaded = this.routes.GetByID(route.OutboundRouteID);
            Assert.NotNull(loaded);
            Assert.Null(loaded!.MohClassID);
        }

        /// <summary>The international guard closes the prepend door too (D47, D109).</summary>
        [Fact]
        public void A_prepend_starting_with_zero_cannot_be_stored()
        {
            var trunkID = AddTrunk();
            var route = Route(trunkID);
            route.PrependDigits = "011";

            var ex = Assert.Throws<ValidationFailedException>(() => this.routes.Insert(route));

            Assert.Contains("international", ex.Message);
            Assert.Empty(this.routes.GetAll());
        }
    }
}
