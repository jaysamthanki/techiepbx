using System.Globalization;
using log4net;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Backup
{
    /// <summary>
    /// <c>tnpbx restore &lt;file&gt;</c> (D171), in order: check the manifest says this is one of
    /// our backups, unpack it into a private staging directory and check it (every payload
    /// present, the database passes integrity_check), refuse a database newer than this build's
    /// schema scripts, stop the web app, swap the database in, migrate it, swap the sounds (and
    /// with a full backup the voicemail spool) in, start the web app and see that it answers.
    ///
    /// Everything up to the stop changes nothing on the box, so a refusal there leaves it exactly
    /// as it was. From the swap on, nothing is deleted: what was there is moved aside to a
    /// <c>.pre-restore</c> name beside it, and the report says where.
    ///
    /// The web app's unit and its health check are hooks so the tests can run every step but
    /// systemctl; ownership is a switch for the same reason, since only root can chown.
    /// </summary>
    public class Restorer
    {
        /// <summary>The suffix of what a restore moved out of the way.</summary>
        public const string AsideSuffix = ".pre-restore";

        /// <summary>
        /// The database's owner and group: the web app's account and its primary group, which is
        /// what app-deploy.sh gives everything under /opt/tnpbx.
        /// </summary>
        public const string DatabaseGroup = "asterisk";

        public const string DatabaseOwner = "tnpbx";

        /// <summary>What sits beside a SQLite database while it is in use, and moves with it.</summary>
        private static readonly string[] DatabaseCompanions = ["-journal", "-wal", "-shm"];

        private static readonly ILog Log = LogManager.GetLogger(typeof(Restorer));

        private readonly Dictionary<string, uint?> groups = new(StringComparer.Ordinal);

        private readonly Dictionary<string, uint?> users = new(StringComparer.Ordinal);

        /// <summary><c>systemctl start|stop tnpbx-web</c>, or what a test does instead.</summary>
        public Action<string> ControlWeb { get; init; } = WebService.Control;

        public InstallLayout Layout { get; }

        /// <summary>Whether restored files get their owners set. Off only in tests, which are not root.</summary>
        public bool SetOwnership { get; init; } = true;

        /// <summary>Whether the web app answers after it is started, or what a test says instead.</summary>
        public Func<bool> WebIsUp { get; init; } = WebService.IsUp;

        public Restorer(InstallLayout layout)
        {
            this.Layout = layout;
        }

        /// <summary>
        /// Restores the backup. Returns whether the web app answered afterwards; throws
        /// <see cref="BackupException"/> for a refusal or a failure, having logged what, if
        /// anything, was replaced.
        /// </summary>
        public bool Run(string archivePath)
        {
            archivePath = Path.GetFullPath(archivePath);
            if (!File.Exists(archivePath))
                throw new BackupException($"There is no file at {archivePath}.");

            Log.Info($"Step 1/8: checking that {archivePath} is a TNPBX backup");
            var manifest = BackupArchive.ReadManifest(archivePath);
            CheckManifest(manifest);
            Log.Info($"  {(manifest.Full ? "Full" : "Config-only")} backup of {manifest.Hostname}, taken {manifest.Created} by {manifest.AppVersion}");

            (uint Uid, uint Gid)? databaseOwner = this.SetOwnership ? DatabaseOwnerIds() : null;

            var staging = BackupArchive.CreateStagingDirectory(this.Layout.StagingRoot, "tnpbx-restore-");

            try
            {
                Log.Info($"Step 2/8: unpacking into {staging} and checking it");
                var entries = BackupArchive.Extract(archivePath, staging);

                foreach (var content in manifest.Contents)
                {
                    if (!entries.ContainsKey(content))
                        throw new BackupException($"The manifest lists '{content}' but the archive does not hold it: the backup is incomplete.");
                }

                var stagedDatabase = Path.Combine(staging, BackupManifest.DatabaseEntry);
                var (integrity, schemaVersion) = BackupWriter.Inspect(stagedDatabase);
                if (integrity != "ok")
                    throw new BackupException($"The backed-up database fails its integrity check: {integrity}");

                Log.Info("  Every payload is present and the database passes integrity_check");

                Log.Info("Step 3/8: checking the schema version");
                var latest = Database.LatestSchemaVersion;
                if (schemaVersion > latest || manifest.SchemaVersion > latest)
                    throw new BackupException(
                        $"The backup's database is at schema version {Math.Max(schemaVersion, manifest.SchemaVersion)} and this build only knows up to {latest}. " +
                        "It was taken by a newer TNPBX, and a database cannot be migrated down: install that version (or newer) first.");

                if (schemaVersion != manifest.SchemaVersion)
                    throw new BackupException(
                        $"The manifest says schema version {manifest.SchemaVersion} but the database in the archive is at {schemaVersion}; refusing a backup that disagrees with itself.");

                Log.Info($"  Schema {schemaVersion}; this build migrates up to {latest}");

                Log.Info($"Step 4/8: stopping {WebService.Unit}");
                this.ControlWeb("stop");

                var replaced = new List<string>();

                try
                {
                    // In production nothing in this process holds the database; in the tests a
                    // pooled connection could still point at the file about to be moved.
                    SqliteConnection.ClearAllPools();

                    Log.Info($"Step 5/8: replacing the database at {this.Layout.DatabasePath}");
                    this.SwapDatabase(stagedDatabase, replaced);

                    Log.Info("Step 6/8: running the schema migrations on the restored database");
                    new Database(this.Layout.DatabasePath).Migrate();
                    SqliteConnection.ClearAllPools();

                    if (databaseOwner is var (uid, gid))
                    {
                        Native.Chown(this.Layout.DatabasePath, uid, gid);
                        Log.Info($"  Owner set to {DatabaseOwner}:{DatabaseGroup}");
                    }

                    // The confs on disk were rendered from the database just replaced; the UI's
                    // "apply pending" banner is what tells the admin to regenerate them.
                    new ConfigPendingMarker(Path.GetDirectoryName(this.Layout.DatabasePath)!).Raise();

                    Log.Info("Step 7/8: replacing the announcement audio" + (manifest.Full ? " and the voicemail spool" : ""));

                    if (manifest.Contents.Contains(BackupManifest.SoundsEntry))
                        this.SwapTree(Path.Combine(staging, BackupManifest.SoundsEntry), this.Layout.SoundsPath, BackupManifest.SoundsEntry, entries, replaced);
                    else
                        Log.Info($"  The backup has no announcement audio; {this.Layout.SoundsPath} left as it is");

                    if (manifest.Contents.Contains(BackupManifest.VoicemailEntry))
                        this.SwapTree(Path.Combine(staging, BackupManifest.VoicemailEntry), this.Layout.VoicemailPath, BackupManifest.VoicemailEntry, entries, replaced);
                    else
                        Log.Info($"  Config-only backup: the voicemail spool at {this.Layout.VoicemailPath} left as it is");
                }
                catch (Exception ex)
                {
                    Log.Error($"The restore failed after {WebService.Unit} was stopped: {ex.Message}");
                    Log.Error(replaced.Count == 0
                        ? "  Nothing had been replaced yet."
                        : "  Already replaced (the originals are beside them):");

                    foreach (var line in replaced)
                        Log.Error("    " + line);

                    Log.Error($"  {WebService.Unit} has been left STOPPED. Move the .pre-restore files back, or fix the cause and restore again, then: systemctl start {WebService.Unit}");
                    throw ex as BackupException ?? new BackupException("The restore did not finish.", ex);
                }

                Log.Info($"Step 8/8: starting {WebService.Unit}");
                bool up;

                // Past the swap a failure here must not lose the report below of what was replaced.
                try
                {
                    this.ControlWeb("start");
                    up = this.WebIsUp();
                }
                catch (BackupException ex)
                {
                    Log.Error(ex.Message);
                    up = false;
                }

                Log.Info("Restore finished. Replaced (the originals are kept beside them):");
                foreach (var line in replaced)
                    Log.Info("  " + line);

                if (up)
                    Log.Info($"{WebService.Unit} is answering. Sign in and press Apply: the Asterisk config on disk still reflects the old database.");
                else
                    Log.Error($"{WebService.Unit} did not answer on port 8080. See: journalctl -u {WebService.Unit} -n 50");

                return up;
            }
            finally
            {
                Directory.Delete(staging, recursive: true);
            }
        }

        /// <summary>Puts back the owner and mode the archive recorded for <paramref name="entryName"/>.</summary>
        private void Apply(string path, string entryName, Dictionary<string, ArchivedEntry> entries)
        {
            if (!entries.TryGetValue(entryName, out var entry))
                return;

            // Owner first: chown can clear the setgid bit the mode is about to set.
            if (this.SetOwnership)
            {
                var uid = Lookup(this.users, entry.UserName, Native.LookupUser) ?? (uint)entry.Uid;
                var gid = Lookup(this.groups, entry.GroupName, Native.LookupGroup) ?? (uint)entry.Gid;
                Native.Chown(path, uid, gid);
            }

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, entry.Mode);
        }

        /// <summary>Where <paramref name="path"/> goes when it is moved out of the way: never onto something already there.</summary>
        private static string AsidePath(string path)
        {
            var aside = path + AsideSuffix;
            if (!File.Exists(aside) && !Directory.Exists(aside))
                return aside;

            return aside + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        }

        private static void CheckManifest(BackupManifest manifest)
        {
            if (manifest.Kind != BackupManifest.BackupKind)
                throw new BackupException(
                    $"This is not a TNPBX backup: its manifest is of kind '{manifest.Kind}', not '{BackupManifest.BackupKind}'. " +
                    "(A FreePBX migration export is imported from the web UI, not restored.)");

            if (manifest.ManifestVersion != BackupManifest.CurrentManifestVersion)
                throw new BackupException(
                    $"This backup's manifest is version {manifest.ManifestVersion}; this build reads version {BackupManifest.CurrentManifestVersion} only.");

            if (!manifest.Contents.Contains(BackupManifest.DatabaseEntry))
                throw new BackupException($"This backup's manifest does not list {BackupManifest.DatabaseEntry}; there is nothing to restore.");
        }

        /// <summary>Copies a staged tree into place, children first, so a read-only mode is set only once nothing more is written under it.</summary>
        private void CopyTree(string source, string target, string entryName, Dictionary<string, ArchivedEntry> entries)
        {
            Directory.CreateDirectory(target);

            foreach (var directory in Directory.EnumerateDirectories(source))
            {
                var name = Path.GetFileName(directory);
                this.CopyTree(directory, Path.Combine(target, name), entryName + "/" + name, entries);
            }

            foreach (var file in Directory.EnumerateFiles(source))
            {
                var name = Path.GetFileName(file);
                var destination = Path.Combine(target, name);

                File.Copy(file, destination);
                this.Apply(destination, entryName + "/" + name, entries);
            }

            this.Apply(target, entryName, entries);
        }

        /// <summary>The database's owner and group IDs, looked up before anything is touched so a box without them refuses early.</summary>
        private static (uint Uid, uint Gid) DatabaseOwnerIds()
        {
            var uid = Native.LookupUser(DatabaseOwner)
                ?? throw new BackupException($"There is no '{DatabaseOwner}' user on this box. Install TNPBX (install.sh, app-deploy.sh) before restoring onto it.");
            var gid = Native.LookupGroup(DatabaseGroup)
                ?? throw new BackupException($"There is no '{DatabaseGroup}' group on this box. Install TNPBX (install.sh, app-deploy.sh) before restoring onto it.");

            return (uid, gid);
        }

        private static uint? Lookup(Dictionary<string, uint?> cache, string name, Func<string, uint?> lookup)
        {
            if (name.Length == 0)
                return null;

            if (!cache.TryGetValue(name, out var id))
                cache[name] = id = lookup(name);

            return id;
        }

        /// <summary>
        /// Moves the live database (and any journal beside it, which belongs to it and would
        /// otherwise be replayed into the restored copy) aside, then puts the staged copy in place
        /// under a temporary name and renames it, so the database path never holds half a file.
        /// </summary>
        private void SwapDatabase(string stagedDatabase, List<string> replaced)
        {
            var database = this.Layout.DatabasePath;
            Directory.CreateDirectory(Path.GetDirectoryName(database)!);

            if (File.Exists(database))
            {
                var aside = AsidePath(database);
                File.Move(database, aside);
                replaced.Add($"{database} -> {aside}");

                foreach (var companion in DatabaseCompanions)
                {
                    if (File.Exists(database + companion))
                        File.Move(database + companion, aside + companion);
                }
            }

            else
            {
                replaced.Add($"{database} (new; nothing was there)");
            }

            var incoming = database + ".restoring";
            File.Copy(stagedDatabase, incoming, overwrite: true);
            File.Move(incoming, database);
        }

        /// <summary>Moves a live directory aside and copies the staged one into its place.</summary>
        private void SwapTree(string staged, string target, string entryName, Dictionary<string, ArchivedEntry> entries, List<string> replaced)
        {
            if (Directory.Exists(target) || File.Exists(target))
            {
                var aside = AsidePath(target);
                Directory.Move(target, aside);
                replaced.Add($"{target} -> {aside}");
            }
            else
            {
                replaced.Add($"{target} (new; nothing was there)");
            }

            this.CopyTree(staged, target, entryName, entries);
            Log.Info($"  {target} restored");
        }
    }
}
