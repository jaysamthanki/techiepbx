using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Schema 020, which moves a phone's registration off its own row and onto its first key
    /// (D121 amended). It is the one migration here that rearranges rows rather than adding a
    /// column, so it is worth running against the shape it will really meet: every script up to
    /// 019, a few rows, then 020.
    ///
    /// The scripts are read out of the Core assembly rather than copied here, so this tests the
    /// script that ships and not a paraphrase of it.
    /// </summary>
    public class PhoneLineMigrationTests : IDisposable
    {
        private const string Mac = "0004f2aabbcc";

        private readonly SqliteConnection connection;
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-migration-").FullName;

        public PhoneLineMigrationTests()
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(this.directory, "tnpbx.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true,
            }.ToString();

            this.connection = new SqliteConnection(connectionString);
            this.connection.Open();

            foreach (var script in Scripts(upTo: 19))
                this.connection.Execute(script);
        }

        public void Dispose()
        {
            this.connection.Dispose();
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        /// <summary>The schema scripts up to and including a version, in order.</summary>
        private static List<string> Scripts(int upTo)
        {
            var assembly = typeof(Database).Assembly;
            var scripts = new List<(int Version, string Sql)>();

            foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("schema/")))
            {
                var fileName = resource["schema/".Length..];
                var version = int.Parse(fileName[..fileName.IndexOf('_')]);

                if (version > upTo)
                    continue;

                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                scripts.Add((version, reader.ReadToEnd()));
            }

            return scripts.OrderBy(s => s.Version).Select(s => s.Sql).ToList();
        }

        private void AddButton(long phoneID, int position, string targetType, string targetValue) =>
            this.connection.Execute(
                "INSERT INTO PhoneButtons (PhoneID, Position, TargetType, TargetValue) VALUES (@phoneID, @position, @targetType, @targetValue)",
                new { phoneID, position, targetType, targetValue });

        private long AddExtension(string number) =>
            this.connection.ExecuteScalar<long>(
                "INSERT INTO Extensions (Number, Name, Secret) VALUES (@number, @number, 'AAAAbbbbCCCCdddd1111'); SELECT last_insert_rowid();",
                new { number });

        private long AddPhone(string mac, long? extensionID) =>
            this.connection.ExecuteScalar<long>(
                "INSERT INTO Phones (Mac, ExtensionID) VALUES (@mac, @extensionID); SELECT last_insert_rowid();",
                new { mac, extensionID });

        /// <summary>One phone's keys as "&lt;position&gt;=&lt;kind&gt;:&lt;target&gt;", in key order.</summary>
        private List<string> Keys(long phoneID) =>
            this.connection.Query<PhoneButton>(
                "SELECT Position, TargetType, TargetValue FROM PhoneButtons WHERE PhoneID = @phoneID ORDER BY Position",
                new { phoneID })
                .Select(button => $"{button.Position}={button.Key}")
                .ToList();

        private void Migrate() => this.connection.Execute(Scripts(upTo: 20)[^1]);

        /// <summary>
        /// The whole point of it: the extension the phone was linked to becomes key 1, and every
        /// key it already had moves down one.
        /// </summary>
        [Fact]
        public void The_phones_extension_becomes_key_one_and_the_rest_shift_down()
        {
            var extensionID = this.AddExtension("1001");
            this.AddExtension("1002");
            var phoneID = this.AddPhone(Mac, extensionID);

            this.AddButton(phoneID, 1, "Extension", "1002");
            this.AddButton(phoneID, 4, "ParkingSlot", "3");

            this.Migrate();

            Assert.Equal(
                new[] { "1=Line:1001", "2=Blf:1002", "5=ParkingSlot:3" },
                this.Keys(phoneID));
        }

        /// <summary>
        /// Eight keys and a registration is nine lines on an eight-key handset, which is the bug
        /// this change is about. The last key is the one with nowhere to go.
        /// </summary>
        [Fact]
        public void A_phone_with_every_key_assigned_loses_its_last_key_to_the_line()
        {
            var extensionID = this.AddExtension("1001");
            var phoneID = this.AddPhone(Mac, extensionID);

            for (var position = 1; position <= 8; position++)
            {
                this.AddExtension($"200{position}");
                this.AddButton(phoneID, position, "Extension", $"200{position}");
            }

            this.Migrate();

            var keys = this.Keys(phoneID);

            Assert.Equal(8, keys.Count);
            Assert.Equal("1=Line:1001", keys[0]);
            Assert.Equal("2=Blf:2001", keys[1]);
            Assert.Equal("8=Blf:2007", keys[7]);
            Assert.DoesNotContain("Blf:2008", keys);
        }

        /// <summary>
        /// A phone nobody had assigned gets no line, and its keys stay exactly where they were:
        /// there is nothing to make room for.
        /// </summary>
        [Fact]
        public void A_phone_with_no_extension_keeps_its_keys_where_they_were()
        {
            this.AddExtension("1002");
            var phoneID = this.AddPhone(Mac, null);

            this.AddButton(phoneID, 1, "Extension", "1002");

            this.Migrate();

            Assert.Equal(new[] { "1=Blf:1002" }, this.Keys(phoneID));
        }

        /// <summary>The column goes with it: two answers to "who is this phone?" is one too many.</summary>
        [Fact]
        public void The_extension_column_is_gone_from_phones()
        {
            this.AddPhone(Mac, null);

            this.Migrate();

            var columns = this.connection.Query<string>("SELECT name FROM pragma_table_info('Phones')").ToList();

            Assert.Contains("Mac", columns);
            Assert.DoesNotContain("ExtensionID", columns);
        }
    }
}
