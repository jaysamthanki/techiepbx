using System.Security.Cryptography;
using System.Text.RegularExpressions;
using log4net;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Core.Migration
{
    /// <summary>
    /// Where an uploaded export is unpacked between the upload, the preview and the import (D170):
    /// one directory per upload under <c>Data/import</c>, beside the database, named by a random
    /// ID the preview carries in its URL. Beside the database rather than in /tmp because the
    /// service has a private /tmp that may well be RAM, and an export is hundreds of megabytes.
    ///
    /// The export holds every extension and trunk secret in plain text, so the directory is the
    /// service user's alone (0700), only the latest upload is ever kept, and it is deleted once
    /// the import is done.
    /// </summary>
    public partial class MigrationStaging
    {
        /// <summary>The folder under the database's directory.</summary>
        public const string DirectoryName = "import";

        private const UnixFileMode PrivateDirectoryMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        private static readonly ILog Log = LogManager.GetLogger(typeof(MigrationStaging));

        /// <summary>The staging root; every upload is one directory under it.</summary>
        public string Root { get; }

        public MigrationStaging(string root)
        {
            this.Root = Path.GetFullPath(root);
        }

        /// <summary>The staging directory beside a database, which is where the app keeps it.</summary>
        public static MigrationStaging Beside(Database database) =>
            new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database.FilePath))!, DirectoryName));

        /// <summary>
        /// Deletes every staged upload and makes a fresh, empty directory for a new one. Returns
        /// its ID.
        /// </summary>
        public string Create()
        {
            this.DeleteAll();

            Directory.CreateDirectory(this.Root);
            SetMode(this.Root);

            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var path = Path.Combine(this.Root, id);

            Directory.CreateDirectory(path);
            SetMode(path);

            return id;
        }

        /// <summary>Removes one staged upload. A missing one is not an error.</summary>
        public void Delete(string id)
        {
            var path = this.PathFor(id);
            if (path != null)
                Directory.Delete(path, recursive: true);
        }

        /// <summary>
        /// After an import that left voicemail to be copied by hand: everything goes but the
        /// voicemail, so the secrets in the manifest do not sit on disk waiting for that.
        /// </summary>
        public void KeepVoicemailOnly(string id)
        {
            var path = this.PathFor(id);
            if (path == null)
                return;

            foreach (var file in Directory.EnumerateFiles(path))
                File.Delete(file);

            var sounds = Path.Combine(path, "files", "sounds");
            if (Directory.Exists(sounds))
                Directory.Delete(sounds, recursive: true);

            Log.Info($"Staged import {id} kept for its voicemail only");
        }

        /// <summary>
        /// The directory for an ID, or null when the ID is not one this class would have made or
        /// that upload is gone. The ID arrives in a URL, so it is matched before it is a path.
        /// </summary>
        public string? PathFor(string? id)
        {
            if (id == null || !IdPattern().IsMatch(id))
                return null;

            var path = Path.Combine(this.Root, id);
            return Directory.Exists(path) ? path : null;
        }

        private void DeleteAll()
        {
            if (!Directory.Exists(this.Root))
                return;

            foreach (var directory in Directory.EnumerateDirectories(this.Root))
                Directory.Delete(directory, recursive: true);

            foreach (var file in Directory.EnumerateFiles(this.Root))
                File.Delete(file);
        }

        private static void SetMode(string path)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, PrivateDirectoryMode);
        }

        [GeneratedRegex(@"^[0-9a-f]{32}\z")]
        private static partial Regex IdPattern();
    }
}
