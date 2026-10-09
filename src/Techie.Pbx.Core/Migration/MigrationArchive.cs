using System.Formats.Tar;
using System.IO.Compression;
using log4net;

namespace Techie.Pbx.Core.Migration
{
    /// <summary>
    /// Unpacks a <c>tnpbx-migrate.tar.gz</c> into a staging directory (D170). The tarball comes
    /// from another machine through a browser, so it is treated the way an upload is: every entry
    /// name is checked before anything is written, and one bad entry refuses the whole archive
    /// rather than being quietly skipped — an export that tries to write outside its directory is
    /// not an export to import half of.
    ///
    /// What is refused: absolute names, any <c>..</c> segment, backslashes, drive letters, NULs,
    /// links of either kind, devices and FIFOs, more than <see cref="MaxEntries"/> entries, and
    /// more than the byte cap of content in total. Only regular files and directories are created.
    /// </summary>
    public static class MigrationArchive
    {
        /// <summary>
        /// The cap on what an export may unpack to. A real FreePBX 16 box with 519 voicemails came
        /// to 150 MB; a gigabyte is several of those, and well short of filling a small VM.
        /// </summary>
        public const long DefaultMaxExtractedBytes = 1024L * 1024 * 1024;

        /// <summary>The same cap on the compressed upload: audio barely compresses.</summary>
        public const long MaxUploadBytes = 1024L * 1024 * 1024;

        /// <summary>Far more files than any real export: one per sound plus two per voicemail.</summary>
        public const int MaxEntries = 50_000;

        private static readonly ILog Log = LogManager.GetLogger(typeof(MigrationArchive));

        /// <summary>
        /// Extracts the gzip tarball in <paramref name="archive"/> into <paramref name="targetDirectory"/>,
        /// which must exist and should be empty. Throws <see cref="MigrationArchiveException"/>
        /// for an archive that breaks any rule; what was already written is left for the caller
        /// to delete with the directory.
        /// </summary>
        public static void Extract(Stream archive, string targetDirectory, long maxExtractedBytes = DefaultMaxExtractedBytes)
        {
            var root = Path.GetFullPath(targetDirectory);
            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;

            long total = 0;
            var entries = 0;

            try
            {
                using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
                using var reader = new TarReader(gzip);

                while (reader.GetNextEntry() is { } entry)
                {
                    if (++entries > MaxEntries)
                        throw new MigrationArchiveException($"The export has more than {MaxEntries} files in it, which no real export has.");

                    var problem = NameProblem(entry.Name);
                    if (problem != null)
                        throw new MigrationArchiveException($"Refusing the export: entry '{Printable(entry.Name)}' {problem}.");

                    var relative = Relative(entry.Name);
                    if (relative.Length == 0)
                        continue;

                    var path = Path.GetFullPath(Path.Combine(root, relative));

                    // The name rules above already make this impossible; this is the last word on
                    // it, so the day somebody loosens them it fails here rather than in /etc.
                    if (!path.StartsWith(root, StringComparison.Ordinal))
                        throw new MigrationArchiveException($"Refusing the export: entry '{Printable(entry.Name)}' would land outside the staging directory.");

                    switch (entry.EntryType)
                    {
                        case TarEntryType.Directory:
                            Directory.CreateDirectory(path);
                            break;

                        case TarEntryType.RegularFile:
                        case TarEntryType.V7RegularFile:
                            total += entry.Length;
                            if (total > maxExtractedBytes)
                                throw new MigrationArchiveException(
                                    $"The export unpacks to more than {maxExtractedBytes / (1024 * 1024)} MB, which is more than this importer accepts.");

                            if (File.Exists(path) || Directory.Exists(path))
                                throw new MigrationArchiveException($"Refusing the export: entry '{Printable(entry.Name)}' appears twice.");

                            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                            Write(entry, path);
                            break;

                        // Metadata entries GNU and PAX tar write for long names and attributes:
                        // TarReader folds them into the entry they describe, so seeing one is fine.
                        case TarEntryType.GlobalExtendedAttributes:
                        case TarEntryType.ExtendedAttributes:
                            break;

                        default:
                            throw new MigrationArchiveException(
                                $"Refusing the export: entry '{Printable(entry.Name)}' is a {entry.EntryType}, and an export holds only files and folders.");
                    }
                }
            }
            catch (InvalidDataException ex)
            {
                throw new MigrationArchiveException("That file is not a gzip tarball. Upload the tnpbx-migrate.tar.gz the exporter wrote.", ex);
            }
            catch (FormatException ex)
            {
                throw new MigrationArchiveException("That file is not a valid tarball: " + ex.Message, ex);
            }

            Log.Info($"Migration export unpacked: {entries} entries, {total} bytes");
        }

        /// <summary>
        /// Why an entry name may not be used, or null when it may. Public so the tests can say
        /// exactly which names are refused.
        /// </summary>
        public static string? NameProblem(string name)
        {
            if (name.Length == 0)
                return "has no name";

            if (name.Length > 512)
                return "has a name longer than any real export's";

            if (name.Contains('\0'))
                return "has a NUL in its name";

            if (name.Contains('\\'))
                return "has a backslash in its name";

            if (name.StartsWith('/') || Path.IsPathRooted(name) || (name.Length > 1 && name[1] == ':'))
                return "is an absolute path";

            if (name.Split('/').Any(segment => segment == ".."))
                return "climbs out of the archive with '..'";

            return null;
        }

        /// <summary>The name without a leading <c>./</c> or trailing slash: how it sits under the root.</summary>
        private static string Relative(string name)
        {
            var relative = name;

            while (relative.StartsWith("./", StringComparison.Ordinal))
                relative = relative[2..];

            return relative == "." ? "" : relative.TrimEnd('/');
        }

        /// <summary>An entry name fit for a message: control characters cannot reach the page or the log.</summary>
        private static string Printable(string name)
        {
            var clean = new string(name.Select(c => char.IsControl(c) ? '?' : c).ToArray());
            return clean.Length <= 120 ? clean : clean[..120] + "…";
        }

        /// <summary>
        /// Copies one file's content, counting what is really written rather than trusting the
        /// header's length: a header that lies about its size must not get past the cap.
        /// </summary>
        private static void Write(TarEntry entry, string path)
        {
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);

            if (entry.DataStream == null)
                return;

            var buffer = new byte[81920];
            long written = 0;
            int read;

            while ((read = entry.DataStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                written += read;
                if (written > entry.Length)
                    throw new MigrationArchiveException($"Refusing the export: entry '{Printable(entry.Name)}' holds more data than its header says.");

                output.Write(buffer, 0, read);
            }
        }
    }
}
