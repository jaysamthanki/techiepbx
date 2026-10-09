using System.Text.Json;
using System.Text.Json.Serialization;

namespace Techie.Pbx.Core.Migration
{
    /// <summary>
    /// <c>manifest.json</c>, the one contract between the FreePBX exporter and this importer
    /// (D170, docs/freepbx-import.md). The importer reads this and nothing else: no FreePBX SQL, no
    /// FreePBX conf syntax. Every field is data — nothing in it is ever executed, and every value
    /// is re-validated by the model it lands in.
    /// </summary>
    public class MigrationManifest
    {
        /// <summary>The manifest version this importer reads. Anything else is refused, not guessed at.</summary>
        public const int SupportedVersion = 1;

        /// <summary>A manifest is a few hundred rows of text; anything near this is not one.</summary>
        public const long MaxBytes = 16L * 1024 * 1024;

        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        public string? Exported { get; set; }
        public List<ManifestExtension>? Extensions { get; set; }
        public List<ManifestInboundRoute>? InboundRoutes { get; set; }
        public int ManifestVersion { get; set; }
        public List<ManifestOutboundRoute>? OutboundRoutes { get; set; }
        public List<ManifestPhone>? Phones { get; set; }
        public List<ManifestSound>? Sounds { get; set; }
        public ManifestSource? Source { get; set; }
        public List<ManifestTrunk>? Trunks { get; set; }

        /// <summary>The exporter's own warnings: rows it found and could not carry.</summary>
        public List<string?>? Warnings { get; set; }

        /// <summary>
        /// Reads a manifest, refusing one that is not version 1 or not JSON at all. The lists are
        /// never null afterwards, so nothing downstream has to ask.
        /// </summary>
        public static MigrationManifest Parse(string json)
        {
            MigrationManifest? manifest;

            try
            {
                manifest = JsonSerializer.Deserialize<MigrationManifest>(json, Options);
            }
            catch (JsonException ex)
            {
                throw new MigrationArchiveException($"manifest.json is not valid JSON: {ex.Message}");
            }

            if (manifest == null)
                throw new MigrationArchiveException("manifest.json is empty.");

            if (manifest.ManifestVersion != SupportedVersion)
                throw new MigrationArchiveException(
                    $"manifest.json is version {manifest.ManifestVersion}; this TNPBX reads version {SupportedVersion} only. " +
                    "Use the exporter that ships with this release.");

            manifest.Extensions ??= new List<ManifestExtension>();
            manifest.InboundRoutes ??= new List<ManifestInboundRoute>();
            manifest.OutboundRoutes ??= new List<ManifestOutboundRoute>();
            manifest.Phones ??= new List<ManifestPhone>();
            manifest.Sounds ??= new List<ManifestSound>();
            manifest.Source ??= new ManifestSource();
            manifest.Trunks ??= new List<ManifestTrunk>();
            manifest.Warnings ??= new List<string?>();

            return manifest;
        }

        /// <summary>Reads <c>manifest.json</c> from an extracted export, size-capped before it is read.</summary>
        public static MigrationManifest Load(string path)
        {
            var file = new FileInfo(path);

            if (!file.Exists)
                throw new MigrationArchiveException("The export has no manifest.json at its top level. Is this a tnpbx-migrate.tar.gz?");

            if (file.Length > MaxBytes)
                throw new MigrationArchiveException("manifest.json is far larger than any real export's.");

            return Parse(File.ReadAllText(path));
        }
    }
}
