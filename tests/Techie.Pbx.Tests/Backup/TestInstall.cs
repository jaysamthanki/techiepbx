using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Backup;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Tests.Backup
{
    /// <summary>
    /// A TNPBX install laid out under a temporary directory: a migrated database with one setting
    /// in it, an announcement, a mailbox with a message, and the backup and staging folders.
    /// </summary>
    public sealed class TestInstall : IDisposable
    {
        public const string SettingKey = "Backup.Test";

        public const string SettingValue = "before";

        public InstallLayout Layout { get; }

        public string Root { get; }

        public TestInstall()
        {
            this.Root = Directory.CreateTempSubdirectory("tnpbx-backup-").FullName;
            this.Layout = new InstallLayout(
                Path.Combine(this.Root, "opt", "Data", "tnpbx.db"),
                Path.Combine(this.Root, "sounds", "tnpbx"),
                Path.Combine(this.Root, "spool", "voicemail"),
                Path.Combine(this.Root, "opt", "backups"),
                Path.Combine(this.Root, "staging"));

            Directory.CreateDirectory(Path.GetDirectoryName(this.Layout.DatabasePath)!);
            var database = new Database(this.Layout.DatabasePath);
            database.Migrate();

            using (var connection = database.Open())
                connection.Execute("INSERT INTO Settings (\"Key\", Value) VALUES (@key, @value)", new { key = SettingKey, value = SettingValue });

            this.WriteFile(Path.Combine(this.Layout.SoundsPath, "announcements", "1", "welcome.wav"), "RIFF welcome");
            this.WriteFile(Path.Combine(this.Layout.VoicemailPath, "default", "1001", "INBOX", "msg0000.txt"), "[message]");
        }

        /// <summary>A gzip tarball at <paramref name="path"/> with whatever <paramref name="write"/> puts in it.</summary>
        public static void Archive(string path, Action<TarWriter> write)
        {
            using var output = File.Create(path);
            using var gzip = new GZipStream(output, CompressionLevel.Fastest);
            using var tar = new TarWriter(gzip, TarEntryFormat.Pax);

            write(tar);
        }

        public static void ArchiveFile(TarWriter tar, string name, string content) =>
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
            });

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.Root, recursive: true);
        }

        /// <summary>A Restorer that never touches systemd, recording what it would have asked for.</summary>
        public Restorer Restorer(List<string> serviceCalls) =>
            new(this.Layout)
            {
                ControlWeb = verb => serviceCalls.Add(verb),
                SetOwnership = false,
                WebIsUp = () => true,
            };

        public void SetSetting(string value)
        {
            using var connection = new Database(this.Layout.DatabasePath).Open();
            connection.Execute("UPDATE Settings SET Value = @value WHERE \"Key\" = @key", new { key = SettingKey, value });
        }

        public string? Setting()
        {
            SqliteConnection.ClearAllPools();

            using var connection = new Database(this.Layout.DatabasePath).Open();
            return connection.ExecuteScalar<string?>("SELECT Value FROM Settings WHERE \"Key\" = @key", new { key = SettingKey });
        }

        public void WriteFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }
}
