using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The "an apply is due" marker (D26), and the repository writes that raise it.
    /// </summary>
    public class ConfigPendingMarkerTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-pending-").FullName;
        private readonly Database database;
        private readonly ExtensionRepository extensions;
        private readonly ConfigPendingMarker marker;

        public ConfigPendingMarkerTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.extensions = new ExtensionRepository(this.database);
            this.marker = new ConfigPendingMarker(this.database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private Extension Insert(string number) =>
            new Extension { Number = number, Name = "Test " + number, Secret = SecretGenerator.Create() };

        [Fact]
        public void Nothing_is_pending_on_a_fresh_install()
        {
            Assert.False(this.marker.IsPending);
        }

        [Fact]
        public void The_marker_lives_beside_the_database()
        {
            Assert.Equal(Path.Combine(this.directory, ConfigPendingMarker.FileName), this.marker.FilePath);
        }

        [Fact]
        public void Raise_then_clear()
        {
            this.marker.Raise();
            Assert.True(this.marker.IsPending);

            this.marker.Clear();
            Assert.False(this.marker.IsPending);
        }

        [Fact]
        public void Raising_twice_and_clearing_twice_are_both_harmless()
        {
            this.marker.Raise();
            this.marker.Raise();
            Assert.True(this.marker.IsPending);

            this.marker.Clear();
            this.marker.Clear();
            Assert.False(this.marker.IsPending);
        }

        [Fact]
        public void Inserting_an_extension_raises_it()
        {
            this.extensions.Insert(this.Insert("1001"));

            Assert.True(this.marker.IsPending);
        }

        [Fact]
        public void Updating_an_extension_raises_it()
        {
            var extension = this.Insert("1001");
            this.extensions.Insert(extension);
            this.marker.Clear();

            extension.Name = "Renamed";
            this.extensions.Update(extension);

            Assert.True(this.marker.IsPending);
        }

        [Fact]
        public void Deleting_an_extension_raises_it()
        {
            var extension = this.Insert("1001");
            this.extensions.Insert(extension);
            this.marker.Clear();

            this.extensions.Delete(extension.ExtensionID);

            Assert.True(this.marker.IsPending);
        }

        /// <summary>A write the repository refused changed nothing, so nothing is due.</summary>
        [Fact]
        public void A_rejected_write_does_not_raise_it()
        {
            Assert.Throws<Techie.Pbx.Core.ValidationFailedException>(() =>
                this.extensions.Insert(new Extension { Number = "no", Name = "", Secret = "short" }));

            Assert.False(this.marker.IsPending);
        }

        [Fact]
        public void Reading_does_not_raise_it()
        {
            this.extensions.Insert(this.Insert("1001"));
            this.marker.Clear();

            this.extensions.GetAll();
            this.extensions.GetByNumber("1001");

            Assert.False(this.marker.IsPending);
        }
    }
}
