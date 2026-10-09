using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using log4net;
using Techie.Pbx.Core.Migration;

namespace Techie.Pbx.Backup
{
    /// <summary>
    /// Reads a backup tarball back. Restore runs as root on a file that may have come from
    /// anywhere, so unpacking follows the migration upload's rules exactly (D170, the names are
    /// checked by <see cref="MigrationArchive.NameProblem"/>): no absolute names, no <c>..</c>,
    /// no backslashes, no links, devices or FIFOs, and one bad entry refuses the whole archive.
    /// Every path written is checked to be inside the staging directory before it is written.
    /// </summary>
    public static class BackupArchive
    {
        private const UnixFileMode PrivateDirectoryMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        private static readonly ILog Log = LogManager.GetLogger(typeof(BackupArchive));

        /// <summary>
        /// A new directory under <paramref name="root"/> only its owner can enter: what is unpacked
        /// into it includes the database and its secrets. The caller deletes it.
        /// </summary>
        public static string CreateStagingDirectory(string root, string prefix)
        {
            Directory.CreateDirectory(root);

            var path = Path.Combine(root, prefix + Guid.NewGuid().ToString("N"));

            if (OperatingSystem.IsWindows())
                Directory.CreateDirectory(path);
            else
                Directory.CreateDirectory(path, PrivateDirectoryMode);

            return path;
        }

        /// <summary>
        /// Unpacks the archive into <paramref name="targetDirectory"/> (which must exist) and
        /// returns the owner and mode recorded for each entry, keyed by its path in the archive.
        /// Throws <see cref="BackupException"/> for an archive that breaks a rule or is damaged;
        /// what was already written is left for the caller to delete with the directory.
        /// </summary>
        public static Dictionary<string, ArchivedEntry> Extract(string archivePath, string targetDirectory)
        {
            var root = Path.GetFullPath(targetDirectory);
            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;

            CheckComplete(archivePath);

            var entries = new Dictionary<string, ArchivedEntry>(StringComparer.Ordinal);
            long total = 0;

            Read(archivePath, reader =>
            {
                while (reader.GetNextEntry() is { } entry)
                {
                    var problem = MigrationArchive.NameProblem(entry.Name);
                    if (problem != null)
                        throw new BackupException($"Refusing the backup: entry '{Printable(entry.Name)}' {problem}.");

                    var relative = Relative(entry.Name);
                    if (relative.Length == 0)
                        continue;

                    var path = Path.GetFullPath(Path.Combine(root, relative));

                    // The name rules already make this impossible; this is the last word on it.
                    if (!path.StartsWith(root, StringComparison.Ordinal))
                        throw new BackupException($"Refusing the backup: entry '{Printable(entry.Name)}' would land outside the staging directory.");

                    switch (entry.EntryType)
                    {
                        case TarEntryType.Directory:
                            Directory.CreateDirectory(path);
                            break;

                        case TarEntryType.RegularFile:
                        case TarEntryType.V7RegularFile:
                            if (File.Exists(path) || Directory.Exists(path))
                                throw new BackupException($"Refusing the backup: entry '{Printable(entry.Name)}' appears twice.");

                            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                                entry.DataStream?.CopyTo(output);

                            total += entry.Length;
                            break;

                        case TarEntryType.GlobalExtendedAttributes:
                        case TarEntryType.ExtendedAttributes:
                            continue;

                        default:
                            throw new BackupException(
                                $"Refusing the backup: entry '{Printable(entry.Name)}' is a {entry.EntryType}, and a backup holds only files and folders.");
                    }

                    var posix = entry as PosixTarEntry;

                    entries[relative] = new ArchivedEntry
                    {
                        Gid = entry.Gid,
                        GroupName = posix?.GroupName ?? "",
                        Mode = entry.Mode,
                        Uid = entry.Uid,
                        UserName = posix?.UserName ?? "",
                    };
                }
            });

            Log.Info($"Unpacked {entries.Count} entries, {total} bytes");
            return entries;
        }

        /// <summary>
        /// The manifest, read without unpacking anything else: it is the first entry of every
        /// backup, so for one of ours this reads a few hundred bytes. Throws
        /// <see cref="BackupException"/> when the file is not a tarball or has no manifest.
        /// </summary>
        public static BackupManifest ReadManifest(string archivePath)
        {
            BackupManifest? manifest = null;

            Read(archivePath, reader =>
            {
                while (manifest == null && reader.GetNextEntry() is { } entry)
                {
                    if (Relative(entry.Name) == BackupManifest.FileName && entry.DataStream != null)
                        manifest = BackupManifest.Parse(entry.DataStream);
                }
            });

            return manifest ?? throw new BackupException($"{archivePath} has no {BackupManifest.FileName}: it is not a TNPBX backup.");
        }

        /// <summary>
        /// Refuses a file cut short. .NET's gzip reader stops quietly at the end of whatever data
        /// it has and never reads the trailer, so a backup truncated on an entry boundary would
        /// unpack "cleanly" minus its tail. The trailer's last four bytes are the uncompressed
        /// length mod 2^32; decompressing the whole file once and comparing catches the cut.
        /// </summary>
        private static void CheckComplete(string archivePath)
        {
            long length = 0;
            uint recorded;

            try
            {
                using (var file = File.OpenRead(archivePath))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                {
                    var buffer = new byte[81920];
                    int read;

                    while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                        length += read;
                }

                using (var file = File.OpenRead(archivePath))
                {
                    var trailer = new byte[4];
                    file.Seek(-4, SeekOrigin.End);
                    file.ReadExactly(trailer);
                    recorded = BinaryPrimitives.ReadUInt32LittleEndian(trailer);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
            {
                throw new BackupException($"{archivePath} is not a readable gzip tarball (damaged or truncated?): {ex.Message}", ex);
            }

            if (recorded != (uint)length)
                throw new BackupException($"{archivePath} is truncated or damaged: its gzip trailer does not match its content.");
        }

        /// <summary>An entry name fit for the console: control characters cannot reach the terminal.</summary>
        private static string Printable(string name)
        {
            var clean = new string(name.Select(c => char.IsControl(c) ? '?' : c).ToArray());
            return clean.Length <= 120 ? clean : clean[..120] + "…";
        }

        /// <summary>
        /// Opens the gzip tarball and hands its reader over, turning every way a damaged or foreign
        /// file fails into one <see cref="BackupException"/>.
        /// </summary>
        private static void Read(string archivePath, Action<TarReader> read)
        {
            try
            {
                using var file = File.OpenRead(archivePath);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var reader = new TarReader(gzip);

                read(reader);
            }
            catch (BackupException)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or EndOfStreamException)
            {
                throw new BackupException($"{archivePath} is not a readable gzip tarball (damaged or truncated?): {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new BackupException($"Could not read {archivePath}: {ex.Message}", ex);
            }
        }

        /// <summary>The name without a leading <c>./</c> or trailing slash: how it sits under the root.</summary>
        private static string Relative(string name)
        {
            var relative = name;

            while (relative.StartsWith("./", StringComparison.Ordinal))
                relative = relative[2..];

            return relative == "." ? "" : relative.TrimEnd('/');
        }
    }
}
