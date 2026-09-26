using Microsoft.Data.Sqlite;
using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Tests.Asterisk
{
    public class AmiSecretTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-amisecret-").FullName;
        private readonly SettingsRepository repository;

        public AmiSecretTests()
        {
            var database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            database.Migrate();
            this.repository = new SettingsRepository(database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, true);
        }

        [Fact]
        public void Ensure_GeneratesOnEmptyBox()
        {
            var generated = AmiSecret.Ensure(this.repository);

            Assert.NotEmpty(generated);
            Assert.Equal(generated, this.repository.Get(SettingsKeys.AmiSecret));
        }

        [Fact]
        public void Ensure_GeneratesOnFirstCall_ThenKeepsTheSameValue()
        {
            var first = AmiSecret.Ensure(this.repository);
            var second = AmiSecret.Ensure(this.repository);

            Assert.Equal(first, second);
        }

        [Fact]
        public void Ensure_NeverOverwritesAnExistingSecret()
        {
            this.repository.Set(SettingsKeys.AmiSecret, "operator-chosen-secret");

            var read = AmiSecret.Ensure(this.repository);

            Assert.Equal("operator-chosen-secret", read);
        }

        [Fact]
        public void Ensure_LeavesAMissingRowAloneWhenWhitespace()
        {
            this.repository.Set(SettingsKeys.AmiSecret, "  ");

            var generated = AmiSecret.Ensure(this.repository);

            Assert.NotEqual("  ", generated);
        }

        [Fact]
        public void Generate_Is32HexCharacters()
        {
            for (var i = 0; i < 100; i++)
            {
                var value = AmiSecret.Generate();

                Assert.Equal(32, value.Length);
                Assert.Matches("^[0-9a-f]+$", value);
            }
        }

        [Fact]
        public void Generate_DoesNotRepeatWithinAnyReasonableRun()
        {
            var values = new HashSet<string>();

            for (var i = 0; i < 1000; i++)
                values.Add(AmiSecret.Generate());

            Assert.Equal(1000, values.Count);
        }

        [Fact]
        public void GeneratedSecretValidates()
        {
            var ami = new AmiSettings { Username = "tnpbx", Secret = AmiSecret.Generate() };

            Assert.DoesNotContain("AMI secret is required.", ami.Validate());
        }

        [Fact]
        public void GeneratedSecretRendersIntoManagerConf()
        {
            var ami = new AmiSettings { Username = "tnpbx", Secret = AmiSecret.Generate() };
            var conf = Techie.Pbx.Asterisk.Config.ManagerConfRenderer.Render(ami);

            Assert.Contains($"secret = {ami.Secret}", conf);
        }
    }
}
