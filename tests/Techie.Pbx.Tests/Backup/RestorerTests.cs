using System.Formats.Tar;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Backup;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Tests.Backup
{
    /// <summary>
    /// <c>tnpbx restore</c> (D171). The refusals all happen before the web app is stopped, so a
    /// refused restore leaves the box exactly as it was; the happy path puts the database, sounds
    /// and spool back and keeps what was there beside them.
    /// </summary>
    public class RestorerTests : IDisposable
    {
        private static readonly DateTime Created = new(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc);

        private readonly TestInstall install = new();

        private readonly List<string> serviceCalls = [];

        public void Dispose()
        {
            this.install.Dispose();
        }

        private string Backup(bool full)
        {
            var output = Path.Combine(this.install.Root, "backup.tar.gz");
            new BackupWriter(this.install.Layout).Write(output, full, Created);
            return output;
        }

        /// <summary>A real backup's database and a manifest the test writes itself.</summary>
        private string BackupWithManifest(BackupManifest manifest, Action<BackupManifest>? change = null)
        {
            var snapshot = Path.Combine(this.install.Root, "snapshot.db");
            BackupWriter.CopyDatabase(this.install.Layout.DatabasePath, snapshot);

            change?.Invoke(manifest);

            var output = Path.Combine(this.install.Root, "crafted.tar.gz");
            TestInstall.Archive(output, tar =>
            {
                TestInstall.ArchiveFile(tar, BackupManifest.FileName, Encoding.UTF8.GetString(manifest.ToJson()));
                tar.WriteEntry(snapshot, BackupManifest.DatabaseEntry);
            });

            return output;
        }

        private static BackupManifest Manifest(long schemaVersion) =>
            new()
            {
                AppVersion = "test",
                Contents = [BackupManifest.DatabaseEntry],
                Created = "2026-10-09T03:00:00Z",
                Hostname = "lab",
                Kind = BackupManifest.BackupKind,
                ManifestVersion = BackupManifest.CurrentManifestVersion,
                SchemaVersion = schemaVersion,
            };

        /// <summary>Asserts a refused restore changed nothing: web never stopped, database the same, staging gone.</summary>
        private void AssertUntouched()
        {
            Assert.Empty(this.serviceCalls);
            Assert.Equal(TestInstall.SettingValue, this.install.Setting());
            Assert.False(File.Exists(this.install.Layout.DatabasePath + Restorer.AsideSuffix));
            Assert.True(!Directory.Exists(this.install.Layout.StagingRoot) || !Directory.EnumerateFileSystemEntries(this.install.Layout.StagingRoot).Any());
        }

        [Fact]
        public void A_full_backup_restores_the_database_sounds_and_spool_and_keeps_what_was_there()
        {
            var backup = this.Backup(full: true);

            this.install.SetSetting("after");
            var welcome = Path.Combine(this.install.Layout.SoundsPath, "announcements", "1", "welcome.wav");
            File.WriteAllText(welcome, "changed");
            this.install.WriteFile(Path.Combine(this.install.Layout.SoundsPath, "announcements", "2", "new.wav"), "RIFF new");
            File.Delete(Path.Combine(this.install.Layout.VoicemailPath, "default", "1001", "INBOX", "msg0000.txt"));

            var up = this.install.Restorer(this.serviceCalls).Run(backup);

            Assert.True(up);
            Assert.Equal(["stop", "start"], this.serviceCalls);

            Assert.Equal(TestInstall.SettingValue, this.install.Setting());
            Assert.True(File.Exists(this.install.Layout.DatabasePath + Restorer.AsideSuffix));
            Assert.True(new ConfigPendingMarker(Path.GetDirectoryName(this.install.Layout.DatabasePath)!).IsPending);

            Assert.Equal("RIFF welcome", File.ReadAllText(welcome));
            Assert.False(File.Exists(Path.Combine(this.install.Layout.SoundsPath, "announcements", "2", "new.wav")));
            Assert.Equal("changed", File.ReadAllText(Path.Combine(this.install.Layout.SoundsPath + Restorer.AsideSuffix, "announcements", "1", "welcome.wav")));

            Assert.True(File.Exists(Path.Combine(this.install.Layout.VoicemailPath, "default", "1001", "INBOX", "msg0000.txt")));
            Assert.True(Directory.Exists(this.install.Layout.VoicemailPath + Restorer.AsideSuffix));

            Assert.Empty(Directory.EnumerateFileSystemEntries(this.install.Layout.StagingRoot));
        }

        [Fact]
        public void A_config_only_backup_leaves_the_spool_alone()
        {
            var backup = this.Backup(full: false);
            var later = Path.Combine(this.install.Layout.VoicemailPath, "default", "1001", "INBOX", "msg0001.txt");
            this.install.WriteFile(later, "[later]");

            this.install.Restorer(this.serviceCalls).Run(backup);

            Assert.True(File.Exists(later));
            Assert.False(Directory.Exists(this.install.Layout.VoicemailPath + Restorer.AsideSuffix));
        }

        [Fact]
        public void Restored_files_get_the_mode_they_were_backed_up_with()
        {
            var welcome = Path.Combine(this.install.Layout.SoundsPath, "announcements", "1", "welcome.wav");
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
            File.SetUnixFileMode(welcome, mode);
            var backup = this.Backup(full: false);
            File.SetUnixFileMode(welcome, mode | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);

            this.install.Restorer(this.serviceCalls).Run(backup);

            Assert.Equal(mode, File.GetUnixFileMode(welcome));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(this.install.Layout.DatabasePath));
        }

        [Fact]
        public void An_older_schema_is_migrated_up_on_the_way_in()
        {
            // A database at schema 0: what the oldest possible backup holds.
            SqliteConnection.ClearAllPools();
            File.Delete(this.install.Layout.DatabasePath);
            using (var empty = new SqliteConnection($"Data Source={this.install.Layout.DatabasePath};Pooling=False"))
            {
                empty.Open();
                empty.Execute("CREATE TABLE Placeholder (PlaceholderID INTEGER PRIMARY KEY)");
            }

            var backup = this.Backup(full: false);
            Assert.Equal(0, BackupArchive.ReadManifest(backup).SchemaVersion);
            new Database(this.install.Layout.DatabasePath).Migrate();

            this.install.Restorer(this.serviceCalls).Run(backup);

            SqliteConnection.ClearAllPools();
            Assert.Equal(Database.LatestSchemaVersion, BackupWriter.Inspect(this.install.Layout.DatabasePath).SchemaVersion);
            Assert.Equal(["stop", "start"], this.serviceCalls);
        }

        [Fact]
        public void A_FreePBX_migration_export_is_refused()
        {
            var export = Path.Combine(this.install.Root, "tnpbx-migrate.tar.gz");
            TestInstall.Archive(export, tar =>
            {
                TestInstall.ArchiveFile(tar, "manifest.json", "{\"manifestVersion\":1,\"source\":{\"freepbxVersion\":\"16\"},\"extensions\":[]}");
                TestInstall.ArchiveFile(tar, "files/sounds/en/custom/Main.wav", "RIFF");
            });

            var thrown = Assert.Throws<BackupException>(() => this.install.Restorer(this.serviceCalls).Run(export));

            Assert.Contains("not a TNPBX backup", thrown.Message);
            this.AssertUntouched();
        }

        [Fact]
        public void A_file_that_is_not_a_tarball_is_refused()
        {
            var bogus = Path.Combine(this.install.Root, "bogus.tar.gz");
            File.WriteAllText(bogus, "this is not gzip");

            Assert.Throws<BackupException>(() => this.install.Restorer(this.serviceCalls).Run(bogus));
            Assert.Empty(this.serviceCalls);
        }

        [Fact]
        public void A_truncated_backup_is_refused()
        {
            // Enough incompressible audio that the cut lands in the middle of the payload.
            var noise = new byte[256 * 1024];
            new Random(49).NextBytes(noise);
            File.WriteAllBytes(Path.Combine(this.install.Layout.SoundsPath, "announcements", "1", "noise.wav"), noise);

            var backup = this.Backup(full: true);
            var bytes = File.ReadAllBytes(backup);
            File.WriteAllBytes(backup, bytes[..(bytes.Length * 2 / 3)]);

            Assert.Throws<BackupException>(() => this.install.Restorer(this.serviceCalls).Run(backup));
            this.AssertUntouched();
        }

        [Fact]
        public void A_backup_cut_off_on_an_entry_boundary_is_refused()
        {
            // .NET's gzip reader does not notice a missing trailer, and losing only the trailer
            // leaves a tarball that unpacks without an error.
            var backup = this.Backup(full: true);
            var bytes = File.ReadAllBytes(backup);
            File.WriteAllBytes(backup, bytes[..^8]);

            var thrown = Assert.Throws<BackupException>(() => this.install.Restorer(this.serviceCalls).Run(backup));

            Assert.Contains("truncated", thrown.Message);
            this.AssertUntouched();
        }

        [Fact]
        public void A_backup_from_a_newer_schema_is_refused()
        {
            var crafted = this.BackupWithManifest(Manifest(Database.LatestSchemaVersion + 1));

            var thrown = Assert.Throws<BackupException>(() => this.install.Restorer(this.serviceCalls).Run(crafted));

            Assert.Contains("cannot be migrated down", thrown.Message);
            this.AssertUntouched();
        }

        [Fact]
        public void A_manifest_that_disagrees_with_its_database_is_refused()
        {
            var crafted = this.BackupWithManifest(Manifest(Database.LatestSchemaVersion - 1));

            Assert.Throws<BackupException>(() => this.install.Restorer(this.serviceCalls).Run(crafted));
            this.AssertUntouched();
        }

        [Fact]
        public void A_newer_manifest_version_is_refused()
        {
            var crafted = this.BackupWithManifest(Manifest(Database.LatestSchemaVersion), manifest => manifest.ManifestVersion = 2);

            var thrown = Assert.Throws<BackupException>(() => this.install.Restorer(this.serviceCalls).Run(crafted));

            Assert.Contains("version 2", thrown.Message);
            this.AssertUntouched();
        }

        [Fact]
        public void A_backup_missing_a_payload_its_manifest_lists_is_refused()
        {
            var crafted = this.BackupWithManifest(
                Manifest(Database.LatestSchemaVersion),
                manifest => manifest.Contents.Add(BackupManifest.SoundsEntry));

            var thrown = Assert.Throws<BackupException>(() => this.install.Restorer(this.serviceCalls).Run(crafted));

            Assert.Contains("incomplete", thrown.Message);
            this.AssertUntouched();
        }

        [Fact]
        public void A_corrupt_database_is_refused()
        {
            var output = Path.Combine(this.install.Root, "corrupt.tar.gz");
            TestInstall.Archive(output, tar =>
            {
                TestInstall.ArchiveFile(tar, BackupManifest.FileName, Encoding.UTF8.GetString(Manifest(Database.LatestSchemaVersion).ToJson()));
                TestInstall.ArchiveFile(tar, BackupManifest.DatabaseEntry, new string('x', 8192));
            });

            var thrown = Assert.Throws<BackupException>(() => this.install.Restorer(this.serviceCalls).Run(output));

            Assert.Contains("integrity", thrown.Message);
            this.AssertUntouched();
        }

        [Theory]
        [InlineData("../escape")]
        [InlineData("sounds/../../escape")]
        [InlineData("/tmp/escape")]
        public void An_entry_that_climbs_out_of_the_staging_directory_is_refused(string name)
        {
            var snapshot = Path.Combine(this.install.Root, "snapshot.db");
            BackupWriter.CopyDatabase(this.install.Layout.DatabasePath, snapshot);

            var output = Path.Combine(this.install.Root, "escape.tar.gz");
            TestInstall.Archive(output, tar =>
            {
                TestInstall.ArchiveFile(tar, BackupManifest.FileName, Encoding.UTF8.GetString(Manifest(Database.LatestSchemaVersion).ToJson()));
                tar.WriteEntry(snapshot, BackupManifest.DatabaseEntry);
                TestInstall.ArchiveFile(tar, name, "escaped");
            });

            var thrown = Assert.Throws<BackupException>(() => this.install.Restorer(this.serviceCalls).Run(output));

            Assert.Contains("Refusing the backup", thrown.Message);
            Assert.False(File.Exists(Path.Combine(this.install.Layout.StagingRoot, "escape")));
            Assert.False(File.Exists(Path.Combine(this.install.Root, "escape")));
            this.AssertUntouched();
        }

        [Fact]
        public void A_symlink_in_the_archive_is_refused()
        {
            var snapshot = Path.Combine(this.install.Root, "snapshot.db");
            BackupWriter.CopyDatabase(this.install.Layout.DatabasePath, snapshot);

            var output = Path.Combine(this.install.Root, "link.tar.gz");
            TestInstall.Archive(output, tar =>
            {
                TestInstall.ArchiveFile(tar, BackupManifest.FileName, Encoding.UTF8.GetString(Manifest(Database.LatestSchemaVersion).ToJson()));
                tar.WriteEntry(snapshot, BackupManifest.DatabaseEntry);
                tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "sounds") { LinkName = "/etc" });
            });

            Assert.Throws<BackupException>(() => this.install.Restorer(this.serviceCalls).Run(output));
            this.AssertUntouched();
        }

        [Fact]
        public void A_missing_file_is_refused()
        {
            Assert.Throws<BackupException>(() => this.install.Restorer(this.serviceCalls).Run(Path.Combine(this.install.Root, "nope.tar.gz")));
            Assert.Empty(this.serviceCalls);
        }
    }
}
