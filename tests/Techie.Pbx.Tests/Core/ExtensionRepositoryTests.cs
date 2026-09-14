using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Tests.Core
{
    public class ExtensionRepositoryTests : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("tnpbx-db-").FullName;
        private readonly ExtensionRepository _repository;

        public ExtensionRepositoryTests()
        {
            var database = new Database(Path.Combine(_directory, "tnpbx.db"));
            database.Migrate();
            _repository = new ExtensionRepository(database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, recursive: true);
        }

        [Fact]
        public void Insert_then_read_back()
        {
            var secret = SecretGenerator.Create();
            var id = _repository.Insert(new Extension { Number = "1001", Name = "Front Desk", Secret = secret });

            var loaded = _repository.GetByNumber("1001");

            Assert.NotNull(loaded);
            Assert.Equal(id, loaded.ExtensionID);
            Assert.Equal("Front Desk", loaded.Name);
            Assert.Equal(secret, loaded.Secret);
            Assert.True(loaded.Enabled);
        }

        [Fact]
        public void GetAll_orders_numerically()
        {
            _repository.Insert(new Extension { Number = "200", Name = "B", Secret = SecretGenerator.Create() });
            _repository.Insert(new Extension { Number = "1000", Name = "C", Secret = SecretGenerator.Create() });
            _repository.Insert(new Extension { Number = "30", Name = "A", Secret = SecretGenerator.Create() });

            Assert.Equal(new[] { "30", "200", "1000" }, _repository.GetAll().Select(e => e.Number));
        }

        [Fact]
        public void Duplicate_number_is_a_validation_error()
        {
            _repository.Insert(new Extension { Number = "1001", Name = "One", Secret = SecretGenerator.Create() });

            var ex = Assert.Throws<ValidationFailedException>(() =>
                _repository.Insert(new Extension { Number = "1001", Name = "Two", Secret = SecretGenerator.Create() }));
            Assert.Contains("already exists", ex.Message);
        }

        [Fact]
        public void Update_and_delete()
        {
            var extension = new Extension { Number = "1001", Name = "Old", Secret = SecretGenerator.Create() };
            _repository.Insert(extension);

            extension.Name = "New";
            extension.Enabled = false;
            _repository.Update(extension);

            var loaded = _repository.GetByNumber("1001")!;
            Assert.Equal("New", loaded.Name);
            Assert.False(loaded.Enabled);

            _repository.Delete(extension.ExtensionID);
            Assert.Null(_repository.GetByNumber("1001"));
        }

        [Fact]
        public void Invalid_extension_is_rejected_before_hitting_the_database()
        {
            var ex = Assert.Throws<ValidationFailedException>(() =>
                _repository.Insert(new Extension { Number = "1x", Name = "Bad\nName", Secret = "short" }));

            Assert.Equal(3, ex.Errors.Count);
            Assert.Empty(_repository.GetAll());
        }

        [Fact]
        public void Migrate_is_idempotent()
        {
            var database = new Database(Path.Combine(_directory, "tnpbx.db"));
            database.Migrate();
            database.Migrate();
        }
    }
}
