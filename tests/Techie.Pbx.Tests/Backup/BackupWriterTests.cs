using System.Formats.Tar;
using System.IO.Compression;
using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Backup;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Tests.Backup
{
    /// <summary>
    /// <c>tnpbx backup</c> (D171): one versioned tarball, manifest first, the database as an
    /// online-backup copy, the sounds, and the voicemail spool only with --full.
    /// </summary>
    public class BackupWriterTests : IDisposable
    {
        private static readonly DateTime Created = new(2026, 10, 9, 12, 34, 56, DateTimeKind.Utc);

        private readonly TestInstall install = new();

        public void Dispose()
        {
            this.install.Dispose();
        }

        private static List<string> EntryNames(string archivePath)
        {
            using var file = File.OpenRead(archivePath);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);

            var names = new List<string>();
            while (reader.GetNextEntry() is { } entry)
                names.Add(entry.Name);

            return names;
        }

        [Fact]
        public void A_backup_has_the_manifest_first_and_every_payload_it_lists()
        {
            var output = Path.Combine(this.install.Root, "out.tar.gz");

            new BackupWriter(this.install.Layout).Write(output, full: true, Created);

            var names = EntryNames(output);
            Assert.Equal(BackupManifest.FileName, names[0]);

            var manifest = BackupArchive.ReadManifest(output);
            Assert.Equal(BackupManifest.BackupKind, manifest.Kind);
            Assert.Equal(1, manifest.ManifestVersion);
            Assert.Equal(Database.LatestSchemaVersion, manifest.SchemaVersion);
            Assert.Equal("2026-10-09T12:34:56Z", manifest.Created);
            Assert.Equal(Environment.MachineName, manifest.Hostname);
            Assert.NotEmpty(manifest.AppVersion);
            Assert.True(manifest.Full);
            Assert.Equal([BackupManifest.DatabaseEntry, BackupManifest.SoundsEntry, BackupManifest.VoicemailEntry], manifest.Contents);

            foreach (var content in manifest.Contents)
                Assert.Contains(content, names);

            Assert.Contains("sounds/announcements/1/welcome.wav", names);
            Assert.Contains("voicemail/default/1001/INBOX/msg0000.txt", names);
        }

        [Fact]
        public void A_config_only_backup_leaves_the_voicemail_spool_out()
        {
            var output = Path.Combine(this.install.Root, "out.tar.gz");

            var manifest = new BackupWriter(this.install.Layout).Write(output, full: false, Created);

            Assert.False(manifest.Full);
            Assert.DoesNotContain(BackupManifest.VoicemailEntry, manifest.Contents);
            Assert.DoesNotContain(EntryNames(output), name => name.StartsWith("voicemail", StringComparison.Ordinal));
            Assert.Contains("sounds/announcements/1/welcome.wav", EntryNames(output));
        }

        [Fact]
        public void A_backup_is_private_to_its_owner_and_leaves_nothing_behind()
        {
            var output = Path.Combine(this.install.Root, "out.tar.gz");

            new BackupWriter(this.install.Layout).Write(output, full: false, Created);

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(output));
            Assert.False(File.Exists(output + ".partial"));
            Assert.Empty(Directory.EnumerateFileSystemEntries(this.install.Layout.StagingRoot));
        }

        [Fact]
        public void The_online_copy_passes_integrity_check_while_the_database_is_held_open()
        {
            var database = new Database(this.install.Layout.DatabasePath);

            using var held = database.Open();
            held.Execute("INSERT INTO Settings (\"Key\", Value) VALUES ('Held.Open', 'yes')");

            // A reader mid-transaction keeps a shared lock on the file for the whole copy.
            using var transaction = held.BeginTransaction();
            Assert.Equal(2, held.ExecuteScalar<long>("SELECT COUNT(*) FROM Settings", transaction: transaction));

            var copy = Path.Combine(this.install.Root, "copy.db");
            BackupWriter.CopyDatabase(this.install.Layout.DatabasePath, copy);

            var (integrity, schemaVersion) = BackupWriter.Inspect(copy);
            Assert.Equal("ok", integrity);
            Assert.Equal(Database.LatestSchemaVersion, schemaVersion);

            using var copied = new SqliteConnection($"Data Source={copy};Pooling=False");
            copied.Open();
            Assert.Equal("yes", copied.ExecuteScalar<string>("SELECT Value FROM Settings WHERE \"Key\" = 'Held.Open'"));
        }

        [Fact]
        public void Inspect_says_what_is_wrong_with_a_file_that_is_not_a_database()
        {
            var bogus = Path.Combine(this.install.Root, "bogus.db");
            File.WriteAllText(bogus, new string('x', 4096));

            var (integrity, _) = BackupWriter.Inspect(bogus);

            Assert.NotEqual("ok", integrity);
        }

        [Fact]
        public void Without_a_database_there_is_no_backup()
        {
            SqliteConnection.ClearAllPools();
            File.Delete(this.install.Layout.DatabasePath);
            var output = Path.Combine(this.install.Root, "out.tar.gz");

            Assert.Throws<BackupException>(() => new BackupWriter(this.install.Layout).Write(output, full: false, Created));
            Assert.False(File.Exists(output));
        }
    }
}
