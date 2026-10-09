using System.Text.Json;

namespace Techie.Pbx.Backup
{
    /// <summary>
    /// <c>manifest.json</c>, the first entry in every backup (D171): what made it, which schema
    /// version the database is at, and which payload paths it carries. The same shape of contract
    /// as the FreePBX migration tarball, with its own <see cref="Kind"/> so neither can be
    /// mistaken for the other.
    /// </summary>
    public class BackupManifest
    {
        /// <summary>The <see cref="Kind"/> every backup this tool writes carries.</summary>
        public const string BackupKind = "tnpbx-backup";

        /// <summary>The manifest format this build writes and is the only one it reads.</summary>
        public const int CurrentManifestVersion = 1;

        /// <summary>The database's payload path.</summary>
        public const string DatabaseEntry = "data/tnpbx.db";

        /// <summary>The manifest's own name in the archive.</summary>
        public const string FileName = "manifest.json";

        /// <summary>The announcement audio's payload path.</summary>
        public const string SoundsEntry = "sounds";

        /// <summary>The voicemail spool's payload path, present only in a <c>--full</c> backup.</summary>
        public const string VoicemailEntry = "voicemail";

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        /// <summary>The version of the build that wrote it. Informational: restore guards on <see cref="SchemaVersion"/>.</summary>
        public string AppVersion { get; set; } = "";

        /// <summary>The payload paths: <see cref="DatabaseEntry"/>, <see cref="SoundsEntry"/> and with --full <see cref="VoicemailEntry"/>.</summary>
        public List<string> Contents { get; set; } = [];

        /// <summary>When the backup was taken, ISO-8601 UTC.</summary>
        public string Created { get; set; } = "";

        public bool Full { get; set; }

        public string Hostname { get; set; } = "";

        public string Kind { get; set; } = "";

        public int ManifestVersion { get; set; }

        /// <summary>The database's <c>PRAGMA user_version</c> when it was copied.</summary>
        public long SchemaVersion { get; set; }

        /// <summary>The manifest in <paramref name="stream"/>, or a <see cref="BackupException"/> when it is not JSON.</summary>
        public static BackupManifest Parse(Stream stream)
        {
            try
            {
                return JsonSerializer.Deserialize<BackupManifest>(stream, Json)
                    ?? throw new BackupException($"{FileName} is empty.");
            }
            catch (JsonException ex)
            {
                throw new BackupException($"{FileName} is not a backup manifest: {ex.Message}", ex);
            }
        }

        public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, Json);
    }
}
