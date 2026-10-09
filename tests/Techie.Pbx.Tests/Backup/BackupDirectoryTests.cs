using Techie.Pbx.Backup;

namespace Techie.Pbx.Tests.Backup
{
    /// <summary>
    /// Retention (D171): the newest seven default-location backups are kept, and nothing that is
    /// not a default-location backup is ever deleted.
    /// </summary>
    public class BackupDirectoryTests : IDisposable
    {
        private readonly TestInstall install = new();

        public void Dispose()
        {
            this.install.Dispose();
        }

        [Fact]
        public void A_backup_is_named_by_when_it_was_taken()
        {
            Assert.Equal("tnpbx-backup-20261009-013405.tar.gz", BackupDirectory.FileName(new DateTime(2026, 10, 9, 1, 34, 5, DateTimeKind.Utc)));
        }

        [Fact]
        public void Prune_keeps_the_newest_seven_and_nothing_else_is_touched()
        {
            var directory = this.install.Layout.BackupsDirectory;
            Directory.CreateDirectory(directory);

            var start = new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
            var names = Enumerable.Range(0, 10).Select(day => BackupDirectory.FileName(start.AddDays(day))).ToList();

            foreach (var name in names)
                File.WriteAllText(Path.Combine(directory, name), "");

            var others = new[] { "before-upgrade.tar.gz", "tnpbx-backup-manual.tar.gz", "tnpbx-backup-20261001-030000.tar.gz.partial" };
            foreach (var other in others)
                File.WriteAllText(Path.Combine(directory, other), "");

            var deleted = BackupDirectory.Prune(directory);

            Assert.Equal(names.Take(3).Select(name => Path.Combine(directory, name)).Order(), deleted.Order());
            Assert.All(names.Skip(3), name => Assert.True(File.Exists(Path.Combine(directory, name))));
            Assert.All(others, other => Assert.True(File.Exists(Path.Combine(directory, other))));
        }

        [Fact]
        public void Default_backups_are_pruned_and_o_backups_are_never()
        {
            var writer = new BackupWriter(this.install.Layout);
            var start = new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);

            // -o backups: one in the default folder under its own name, one elsewhere.
            var besideDefaults = Path.Combine(this.install.Layout.BackupsDirectory, "before-upgrade.tar.gz");
            var elsewhere = Path.Combine(this.install.Root, "offsite", "tnpbx-backup-20250101-000000.tar.gz");
            Directory.CreateDirectory(this.install.Layout.BackupsDirectory);
            writer.Write(besideDefaults, full: false, start.AddDays(-1));
            writer.Write(elsewhere, full: false, start.AddDays(-1));

            var written = Enumerable.Range(0, 9).Select(day => writer.WriteDefault(full: false, start.AddDays(day))).ToList();

            Assert.All(written.Take(2), path => Assert.False(File.Exists(path)));
            Assert.All(written.Skip(2), path => Assert.True(File.Exists(path)));
            Assert.True(File.Exists(besideDefaults));
            Assert.True(File.Exists(elsewhere));
        }

        [Fact]
        public void The_default_folder_is_private()
        {
            new BackupWriter(this.install.Layout).WriteDefault(full: false, DateTime.UtcNow);

            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(this.install.Layout.BackupsDirectory));
        }

        [Fact]
        public void A_symlinked_default_folder_is_refused()
        {
            var real = Path.Combine(this.install.Root, "elsewhere");
            Directory.CreateDirectory(real);
            Directory.CreateDirectory(Path.GetDirectoryName(this.install.Layout.BackupsDirectory)!);
            Directory.CreateSymbolicLink(this.install.Layout.BackupsDirectory, real);

            Assert.Throws<BackupException>(() => BackupDirectory.Prepare(this.install.Layout.BackupsDirectory));
        }
    }
}
