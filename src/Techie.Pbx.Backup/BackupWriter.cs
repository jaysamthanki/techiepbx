using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using Dapper;
using log4net;
using Microsoft.Data.Sqlite;

namespace Techie.Pbx.Backup
{
    /// <summary>
    /// Writes one backup tarball (D171): <c>manifest.json</c>, then the database as a SQLite
    /// online-backup copy, then the announcement audio, then with <c>--full</c> the voicemail
    /// spool. Nothing apply re-derives — generated confs, provisioning output, logs — goes in.
    ///
    /// The archive is streamed straight to disk through gzip, one file at a time, so a spool of
    /// hundreds of megabytes never sits in memory. It is written under a <c>.partial</c> name and
    /// renamed when complete, so a backup that failed half way is never mistaken for one that
    /// did not (or counted by retention).
    /// </summary>
    public class BackupWriter
    {
        /// <summary>Root only: the archive holds every SIP secret on the box.</summary>
        private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        private static readonly ILog Log = LogManager.GetLogger(typeof(BackupWriter));

        private readonly InstallLayout layout;

        public BackupWriter(InstallLayout layout)
        {
            this.layout = layout;
        }

        /// <summary>
        /// Copies a live database with SQLite's online backup API, which takes a consistent
        /// snapshot however many connections have it open — a file copy of a database being
        /// written to is not a database. Pooling is off so neither file stays open afterwards.
        /// </summary>
        public static void CopyDatabase(string sourcePath, string destinationPath)
        {
            using var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());

            using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());

            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }

        /// <summary>
        /// <c>PRAGMA integrity_check</c>'s verdict on a database file, and its schema version.
        /// "ok" is the only answer that means it is sound.
        /// </summary>
        public static (string Integrity, long SchemaVersion) Inspect(string databasePath)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());

            try
            {
                connection.Open();
                var integrity = string.Join("; ", connection.Query<string>("PRAGMA integrity_check"));
                var version = connection.ExecuteScalar<long>("PRAGMA user_version");
                return (integrity, version);
            }
            catch (SqliteException ex)
            {
                return (ex.Message, 0);
            }
        }

        /// <summary>
        /// Writes the backup to <paramref name="outputPath"/>, replacing a file already there, and
        /// returns its manifest.
        /// </summary>
        public BackupManifest Write(string outputPath, bool full, DateTime createdUtc)
        {
            if (!File.Exists(this.layout.DatabasePath))
                throw new BackupException($"There is no database at {this.layout.DatabasePath}; nothing to back up.");

            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var partial = outputPath + ".partial";
            var staging = BackupArchive.CreateStagingDirectory(this.layout.StagingRoot, "tnpbx-backup-");

            try
            {
                var snapshot = Path.Combine(staging, "tnpbx.db");

                Log.Info($"Copying the database from {this.layout.DatabasePath} (online backup)");
                CopyDatabase(this.layout.DatabasePath, snapshot);

                var (integrity, schemaVersion) = Inspect(snapshot);
                if (integrity != "ok")
                    throw new BackupException($"The database copy fails its integrity check ({integrity}); a backup of it would not restore.");

                var manifest = new BackupManifest
                {
                    AppVersion = typeof(BackupWriter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "",
                    Created = createdUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Full = full,
                    Hostname = Environment.MachineName,
                    Kind = BackupManifest.BackupKind,
                    ManifestVersion = BackupManifest.CurrentManifestVersion,
                    SchemaVersion = schemaVersion,
                };

                manifest.Contents.Add(BackupManifest.DatabaseEntry);

                if (Directory.Exists(this.layout.SoundsPath))
                    manifest.Contents.Add(BackupManifest.SoundsEntry);
                else
                    Log.Warn($"There is no announcement audio at {this.layout.SoundsPath}; the backup has none.");

                if (full)
                {
                    if (Directory.Exists(this.layout.VoicemailPath))
                        manifest.Contents.Add(BackupManifest.VoicemailEntry);
                    else
                        Log.Warn($"There is no voicemail spool at {this.layout.VoicemailPath}; the backup has none.");
                }

                // CreateNew, not Create: if anything already sits at this name — a symlink above
                // all — the backup fails instead of root writing through it.
                File.Delete(partial);

                var options = new FileStreamOptions { Access = FileAccess.Write, Mode = FileMode.CreateNew };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = PrivateFileMode;

                using (var output = new FileStream(partial, options))
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
                {
                    tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, BackupManifest.FileName)
                    {
                        DataStream = new MemoryStream(manifest.ToJson()),
                        Mode = PrivateFileMode,
                        ModificationTime = createdUtc,
                    });

                    tar.WriteEntry(snapshot, BackupManifest.DatabaseEntry);

                    if (manifest.Contents.Contains(BackupManifest.SoundsEntry))
                        this.AddTree(tar, this.layout.SoundsPath, BackupManifest.SoundsEntry);

                    if (manifest.Contents.Contains(BackupManifest.VoicemailEntry))
                        this.AddTree(tar, this.layout.VoicemailPath, BackupManifest.VoicemailEntry);
                }

                File.Move(partial, outputPath, overwrite: true);

                Log.Info($"Backup written: {outputPath} ({new FileInfo(outputPath).Length} bytes, schema {schemaVersion}, {(full ? "full" : "config only")})");
                return manifest;
            }
            finally
            {
                File.Delete(partial);
                Directory.Delete(staging, recursive: true);
            }
        }

        /// <summary>
        /// Writes a backup into the default folder under its dated name, then applies retention
        /// there (D171). Returns the backup's path.
        /// </summary>
        public string WriteDefault(bool full, DateTime createdUtc)
        {
            BackupDirectory.Prepare(this.layout.BackupsDirectory);

            var path = Path.Combine(this.layout.BackupsDirectory, BackupDirectory.FileName(createdUtc));
            this.Write(path, full, createdUtc);

            BackupDirectory.Prune(this.layout.BackupsDirectory);
            return path;
        }

        /// <summary>
        /// A directory and everything under it, each entry with its own owner and mode so restore
        /// can put them back exactly. Symbolic links are skipped with a warning — restore refuses
        /// an archive that has any — and a file that disappears while the backup runs (a voicemail
        /// being deleted) is skipped rather than failing the whole backup.
        /// </summary>
        private void AddTree(TarWriter tar, string sourceDirectory, string entryName)
        {
            tar.WriteEntry(sourceDirectory, entryName);

            IEnumerable<FileSystemInfo> children;

            try
            {
                children = new DirectoryInfo(sourceDirectory).EnumerateFileSystemInfos().OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
            }
            catch (DirectoryNotFoundException)
            {
                Log.Warn($"{sourceDirectory} disappeared during the backup; skipped");
                return;
            }

            foreach (var child in children)
            {
                var childEntry = entryName + "/" + child.Name;

                if (child.LinkTarget != null)
                {
                    Log.Warn($"Skipped {child.FullName}: it is a symbolic link, and a backup holds only files and folders");
                    continue;
                }

                if (child is DirectoryInfo)
                {
                    this.AddTree(tar, child.FullName, childEntry);
                    continue;
                }

                try
                {
                    tar.WriteEntry(child.FullName, childEntry);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    Log.Warn($"{child.FullName} disappeared during the backup; skipped");
                }
            }
        }
    }
}
