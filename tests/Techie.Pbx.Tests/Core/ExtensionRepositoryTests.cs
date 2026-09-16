using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Tests.Core
{
    public class ExtensionRepositoryTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-db-").FullName;
        private readonly ExtensionRepository repository;

        public ExtensionRepositoryTests()
        {
            var database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            database.Migrate();
            this.repository = new ExtensionRepository(database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        [Fact]
        public void Insert_then_read_back()
        {
            var secret = SecretGenerator.Create();
            var id = this.repository.Insert(new Extension { Number = "1001", Name = "Front Desk", Secret = secret });

            var loaded = this.repository.GetByNumber("1001");

            Assert.NotNull(loaded);
            Assert.Equal(id, loaded.ExtensionID);
            Assert.Equal("Front Desk", loaded.Name);
            Assert.Equal(secret, loaded.Secret);
            Assert.True(loaded.Enabled);
        }

        [Fact]
        public void GetAll_orders_numerically()
        {
            this.repository.Insert(new Extension { Number = "200", Name = "B", Secret = SecretGenerator.Create() });
            this.repository.Insert(new Extension { Number = "1000", Name = "C", Secret = SecretGenerator.Create() });
            this.repository.Insert(new Extension { Number = "30", Name = "A", Secret = SecretGenerator.Create() });

            Assert.Equal(new[] { "30", "200", "1000" }, this.repository.GetAll().Select(e => e.Number));
        }

        /// <summary>The UI works by ExtensionID, because the number is editable.</summary>
        [Fact]
        public void GetByID_reads_one_extension_and_answers_null_for_one_that_is_gone()
        {
            var id = this.repository.Insert(new Extension { Number = "1001", Name = "Front Desk", Secret = SecretGenerator.Create() });

            var loaded = this.repository.GetByID(id);

            Assert.NotNull(loaded);
            Assert.Equal("1001", loaded.Number);
            Assert.Null(this.repository.GetByID(id + 1));
        }

        [Fact]
        public void Duplicate_number_is_a_validation_error()
        {
            this.repository.Insert(new Extension { Number = "1001", Name = "One", Secret = SecretGenerator.Create() });

            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.repository.Insert(new Extension { Number = "1001", Name = "Two", Secret = SecretGenerator.Create() }));
            Assert.Contains("already exists", ex.Message);
        }

        [Fact]
        public void Update_and_delete()
        {
            var extension = new Extension { Number = "1001", Name = "Old", Secret = SecretGenerator.Create() };
            this.repository.Insert(extension);

            extension.Name = "New";
            extension.Enabled = false;
            this.repository.Update(extension);

            var loaded = this.repository.GetByNumber("1001")!;
            Assert.Equal("New", loaded.Name);
            Assert.False(loaded.Enabled);

            this.repository.Delete(extension.ExtensionID);
            Assert.Null(this.repository.GetByNumber("1001"));
        }

        [Fact]
        public void Invalid_extension_is_rejected_before_hitting_the_database()
        {
            var ex = Assert.Throws<ValidationFailedException>(() =>
                this.repository.Insert(new Extension { Number = "1x", Name = "Bad\nName", Secret = "short" }));

            Assert.Equal(3, ex.Errors.Count);
            Assert.Empty(this.repository.GetAll());
        }

        [Fact]
        public void Migrate_is_idempotent()
        {
            var database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            database.Migrate();
            database.Migrate();
        }
    }
}
