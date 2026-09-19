using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The "Asterisk is still running config we have already replaced on disk" marker (D104). It
    /// is what keeps the restart banner up after an admin declines the restart an apply offered,
    /// so it has to survive the page, and it has to be a different answer from "an apply is due".
    /// </summary>
    public class AsteriskRestartMarkerTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-restart-").FullName;
        private readonly Database database;
        private readonly ConfigPendingMarker pending;
        private readonly AsteriskRestartMarker marker;

        public AsteriskRestartMarkerTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.marker = new AsteriskRestartMarker(this.database);
            this.pending = new ConfigPendingMarker(this.database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        [Fact]
        public void No_restart_is_owed_on_a_fresh_install()
        {
            Assert.False(this.marker.IsPending);
        }

        [Fact]
        public void The_marker_lives_beside_the_database()
        {
            Assert.Equal(Path.Combine(this.directory, AsteriskRestartMarker.FileName), this.marker.FilePath);
        }

        /// <summary>
        /// A restart being owed and an apply being due are two different questions, and an apply
        /// that wrote a startup-only file makes them different answers: it clears the one and
        /// raises the other.
        /// </summary>
        [Fact]
        public void It_is_a_separate_file_from_the_apply_marker()
        {
            Assert.NotEqual(this.pending.FilePath, this.marker.FilePath);

            this.marker.Raise();

            Assert.True(this.marker.IsPending);
            Assert.False(this.pending.IsPending);
        }

        [Fact]
        public void Raise_then_clear()
        {
            this.marker.Raise();
            Assert.True(this.marker.IsPending);

            this.marker.Clear();
            Assert.False(this.marker.IsPending);
        }

        /// <summary>
        /// A second apply that owes a restart while one is already owed, and a restart of an
        /// Asterisk that owed nothing: both are ordinary, so neither may throw.
        /// </summary>
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

        /// <summary>
        /// It is a file, so the answer outlives the object that wrote it — and the process, which
        /// is the point: a declined restart is still owed after a deploy.
        /// </summary>
        [Fact]
        public void A_new_marker_object_sees_what_the_last_one_raised()
        {
            this.marker.Raise();

            Assert.True(new AsteriskRestartMarker(this.directory).IsPending);
        }
    }
}
