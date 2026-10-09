using System.Text.Json;
using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Backup
{
    /// <summary>
    /// Where everything a backup holds lives on this box. The database and the announcement audio
    /// come from the same appsettings.json the web app reads, resolved by the same rule
    /// (<see cref="Database.ResolvePath"/>), so the two can never disagree about which file is the
    /// database; the voicemail spool is Asterisk's fixed one.
    /// </summary>
    public class InstallLayout
    {
        /// <summary>The web app's settings file on an installed box (app-deploy.sh).</summary>
        public const string AppSettingsPath = "/opt/tnpbx/appsettings.json";

        /// <summary>The folder under the install that nightly backups land in (D171).</summary>
        public const string BackupsDirectoryName = "backups";

        /// <summary>
        /// Where a restore unpacks. Not /tmp: on Debian 13 that is RAM, and a full backup's spool
        /// is hundreds of megabytes.
        /// </summary>
        public const string DefaultStagingRoot = "/var/tmp";

        public string BackupsDirectory { get; }

        public string DatabasePath { get; }

        public string SoundsPath { get; }

        public string StagingRoot { get; }

        public string VoicemailPath { get; }

        public InstallLayout(string databasePath, string soundsPath, string voicemailPath, string backupsDirectory, string stagingRoot)
        {
            this.BackupsDirectory = Path.GetFullPath(backupsDirectory);
            this.DatabasePath = Path.GetFullPath(databasePath);
            this.SoundsPath = Path.GetFullPath(soundsPath);
            this.StagingRoot = Path.GetFullPath(stagingRoot);
            this.VoicemailPath = Path.GetFullPath(voicemailPath);
        }

        /// <summary>
        /// The layout an installed box has, read from its appsettings.json. The install folder is
        /// the one the file is in: that is the web app's content root (WorkingDirectory in its unit).
        /// </summary>
        public static InstallLayout Read(string appSettingsPath)
        {
            var contentRoot = Path.GetDirectoryName(Path.GetFullPath(appSettingsPath))!;
            JsonDocument settings;

            try
            {
                using var stream = File.OpenRead(appSettingsPath);
                settings = JsonDocument.Parse(stream, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                throw new BackupException($"Could not read {appSettingsPath}: {ex.Message}", ex);
            }

            using (settings)
            {
                return new InstallLayout(
                    Database.ResolvePath(Setting(settings, Database.PathSetting), Database.DefaultPath, contentRoot),
                    Database.ResolvePath(Setting(settings, AnnouncementStore.PathSetting), AnnouncementStore.DefaultSoundsPath, contentRoot),
                    VoicemailSpool.Root,
                    Path.Combine(contentRoot, BackupsDirectoryName),
                    DefaultStagingRoot);
            }
        }

        /// <summary>
        /// A <c>Section:Key</c> setting, matched without regard to case as ASP.NET configuration
        /// matches it, or null when it is not there or not a string.
        /// </summary>
        private static string? Setting(JsonDocument settings, string key)
        {
            var current = settings.RootElement;

            foreach (var part in key.Split(':'))
            {
                if (current.ValueKind != JsonValueKind.Object)
                    return null;

                var next = current.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, part, StringComparison.OrdinalIgnoreCase));
                if (next.Value.ValueKind == JsonValueKind.Undefined)
                    return null;

                current = next.Value;
            }

            return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
        }
    }
}
