using log4net;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Audio
{
    /// <summary>
    /// The music on hold audio on disk: one directory per class, one converted WAV per track
    /// (D119, D122). The web user writes these files; the <c>asterisk</c> user reads them through
    /// its group, exactly as it reads the announcements and the generated conf files (D18, D55).
    ///
    /// A directory per class rather than one flat one, because <c>res_musiconhold</c> in
    /// <c>files</c> mode plays a directory: the class names it and Asterisk reads everything in it,
    /// so two classes that shared a directory would be the same music under two names. The base
    /// path is a constant rather than a setting for the same reason it was under D119 — the
    /// generated musiconhold.conf names it, so moving it in one place only would leave Asterisk
    /// looking somewhere else.
    ///
    /// Every path this class produces is built from a row ID, a class directory and a file name it
    /// derived itself, each matched against a strict pattern, and is then checked to be inside the
    /// base directory before anything is written or deleted. Nothing from the browser — least of
    /// all the uploaded file name — ever reaches a path.
    /// </summary>
    public class MohStore
    {
        /// <summary>
        /// Where the classes go. The same path the generated musiconhold.conf names, one
        /// subdirectory down, and the one the installer creates setgid asterisk (D119).
        /// </summary>
        public const string DefaultMohPath = "/var/lib/asterisk/moh";

        /// <summary>
        /// The cap on an upload, before conversion. Bigger than an announcement's, because hold
        /// music is minutes rather than seconds, and still small enough that refusing early is
        /// what keeps a large file from being spooled and handed to ffmpeg at all.
        /// </summary>
        public const long MaxUploadBytes = 40L * 1024 * 1024;

        /// <summary>Owner read/write, group read: the asterisk user reads through its group (D18).</summary>
        private const UnixFileMode AudioFileMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

        /// <summary>
        /// The same, plus the execute bits a directory needs to be entered, plus setgid — so that
        /// a file created inside is group-owned by asterisk rather than by the web user's own
        /// group. The installer creates the base directory; this mode is what a class directory
        /// made here gets, and what the base gets on a development box.
        /// </summary>
        private const UnixFileMode AudioDirectoryMode =
            UnixFileMode.SetGroup |
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute;

        private static readonly ILog Log = LogManager.GetLogger(typeof(MohStore));

        private readonly AudioConverter converter;

        /// <summary>The base directory. Every class directory is one level inside it.</summary>
        public string MohPath { get; }

        public MohStore()
            : this(DefaultMohPath, new AudioConverter(AudioConverter.DefaultProgram, 120, AudioConverter.MohG722))
        {
        }

        public MohStore(string mohPath)
            : this(mohPath, new AudioConverter(AudioConverter.DefaultProgram, 120, AudioConverter.MohG722))
        {
        }

        public MohStore(string mohPath, AudioConverter converter)
        {
            this.MohPath = string.IsNullOrWhiteSpace(mohPath) ? DefaultMohPath : mohPath.Trim();
            this.converter = converter;
        }

        /// <summary>The full path of one class's directory, checked to be inside the base path.</summary>
        public string ClassPath(MohClass mohClass) =>
            this.Inside(Path.Combine(Path.GetFullPath(this.MohPath), DirectoryName(mohClass)));

        /// <summary>
        /// The directory one class plays, written the way a conf file names it. Static because the
        /// renderer needs the same answer without a store to ask (D122), and it re-checks the
        /// directory name rather than trusting the row it came out of.
        /// </summary>
        public static string ConfDirectory(MohClass mohClass) => $"{DefaultMohPath}/{DirectoryName(mohClass)}";

        /// <summary>
        /// Makes a new class's directory, so that the class Asterisk is about to be told about has
        /// somewhere to read. A class with no tracks is still written into musiconhold.conf, and
        /// <c>res_musiconhold</c> warns about a directory it cannot enter.
        /// </summary>
        public void CreateClass(MohClass mohClass) => this.EnsureDirectory(mohClass);

        /// <summary>
        /// Removes one track's file. Called when the row is deleted, so that deleting a track does
        /// not leave music playing to callers for ever.
        /// </summary>
        public void Delete(MohClass mohClass, MohFile file)
        {
            if (!file.HasAudio || !MohFile.IsValidFile(file.File))
                return;

            var path = this.PathFor(mohClass, file.MohFileID, file.File);

            if (!File.Exists(path))
                return;

            File.Delete(path);
            Log.Info($"Removed the music on hold file for track {file.MohFileID}");
        }

        /// <summary>
        /// Removes a whole class's directory and everything in it, for when the class itself is
        /// deleted. The rows go with it through the foreign key; the files have to be taken out
        /// here or they are music in a directory nothing names.
        /// </summary>
        public void DeleteClass(MohClass mohClass)
        {
            var path = this.ClassPath(mohClass);

            if (!Directory.Exists(path))
                return;

            Directory.Delete(path, recursive: true);
            Log.Info($"Removed the music on hold directory for class '{mohClass.Name}'");
        }

        /// <summary>
        /// What is on disk for this track, or null when the row names a file that is not there. A
        /// row and a file can drift apart — a failed write, a restored backup, a hand edit — and
        /// the UI says so rather than showing a length for nothing.
        /// </summary>
        public AnnouncementAudio? Describe(MohClass mohClass, MohFile file)
        {
            if (!file.HasAudio || !MohFile.IsValidFile(file.File))
                return null;

            var path = this.PathFor(mohClass, file.MohFileID, file.File);
            if (!File.Exists(path))
                return null;

            return new AnnouncementAudio
            {
                Bytes = new FileInfo(path).Length,
                FileName = file.File,
            };
        }

        /// <summary>The full path of one track's audio, checked to be inside the base path.</summary>
        public string PathFor(MohClass mohClass, long mohFileID, string fileName)
        {
            if (mohFileID <= 0)
                throw new InvalidOperationException("A music on hold track has to be saved before its audio can be.");

            if (!MohFile.IsValidFile(fileName))
                throw new InvalidOperationException($"Refusing to use '{fileName}' as a music on hold file name.");

            return this.Inside(Path.Combine(Path.GetFullPath(this.MohPath), DirectoryName(mohClass), fileName));
        }

        /// <summary>
        /// The track's existing audio under a new name, for when the track itself was renamed. The
        /// file is named after the track, so a rename that left the old file there would leave the
        /// directory holding music nobody could explain — and Asterisk would play both.
        ///
        /// Both names are taken as arguments rather than read off the row, because during a rename
        /// the row already says the new one and the disk still says the old.
        /// </summary>
        public void Rename(MohClass mohClass, long mohFileID, string fromFileName, string toFileName)
        {
            var from = this.PathFor(mohClass, mohFileID, fromFileName);
            var to = this.PathFor(mohClass, mohFileID, toFileName);

            if (string.Equals(from, to, StringComparison.Ordinal) || !File.Exists(from))
                return;

            File.Move(from, to, overwrite: true);
            SetMode(to, AudioFileMode);

            Log.Info($"Music on hold track {mohFileID} renamed to {toFileName}");
        }

        /// <summary>
        /// Moves a class's music when its directory is changed, so that the tracks follow the class
        /// rather than being left where the conf file no longer looks.
        /// </summary>
        public void RenameClass(MohClass from, MohClass to)
        {
            var source = this.ClassPath(from);
            var target = this.ClassPath(to);

            if (string.Equals(source, target, StringComparison.Ordinal) || !Directory.Exists(source))
                return;

            if (Directory.Exists(target))
                throw new IOException($"There is already a directory at {target}.");

            Directory.Move(source, target);
            Log.Info($"Music on hold class '{to.Name}' now plays {target}");
        }

        /// <summary>
        /// Takes an upload, converts it, and makes it the track's audio. Returns the file name that
        /// was stored, which is derived from the track's ID and name and never from the browser's.
        ///
        /// The conversion happens in a temporary directory first, so an upload that ffmpeg refuses
        /// — or an ffmpeg that is not installed — leaves whatever the track already had completely
        /// untouched.
        /// </summary>
        public string Save(MohClass mohClass, MohFile file, Stream upload, string previousFile = "")
        {
            var fileName = MohFile.FileNameFor(file.MohFileID, file.Name);
            var target = this.PathFor(mohClass, file.MohFileID, fileName);
            var work = Directory.CreateTempSubdirectory("tnpbx-moh-");

            try
            {
                var source = Path.Combine(work.FullName, "upload");
                var converted = Path.Combine(work.FullName, fileName);

                var container = Spool(upload, source);
                this.converter.ToPrompt(source, converted);

                this.EnsureDirectory(mohClass);

                File.Move(converted, target, overwrite: true);
                SetMode(target, AudioFileMode);

                // One track, one file. Replacing the audio, or renaming the track in the same
                // save, must not leave a second copy in a directory Asterisk plays whole. The
                // sweep catches what this system named; the previous file is passed in as well,
                // because a shipped track's file was named by the installer (D122).
                var stale = Directory.EnumerateFiles(Path.GetDirectoryName(target)!, $"{file.MohFileID}-*").ToList();

                if (previousFile.Length > 0 && MohFile.IsValidFile(previousFile))
                    stale.Add(this.PathFor(mohClass, file.MohFileID, previousFile));

                foreach (var other in stale)
                {
                    if (!string.Equals(other, target, StringComparison.Ordinal) && File.Exists(other))
                        File.Delete(other);
                }

                Log.Info($"Music on hold track {file.MohFileID} stored as {mohClass.Directory}/{fileName} " +
                         $"(from {AudioSignature.Describe(container)}, {new FileInfo(target).Length} bytes)");

                return fileName;
            }
            finally
            {
                work.Delete(recursive: true);
            }
        }

        /// <summary>The class's directory name, refused unless it is one this system would write.</summary>
        private static string DirectoryName(MohClass mohClass)
        {
            if (!MohClass.IsValidDirectory(mohClass.Directory))
                throw new InvalidOperationException($"Refusing to use '{mohClass.Directory}' as a music on hold directory.");

            return mohClass.Directory;
        }

        /// <summary>
        /// Creates the class's directory if it is not there, and sets the mode only when it had to
        /// create it. On a real install the base directory was made by the installer, owned
        /// <c>asterisk:asterisk</c> and setgid so that what the web user writes is group-readable
        /// by Asterisk (D119); a class directory created here inherits that group and is given the
        /// same mode, and taking a chmod to a directory that already existed would undo it.
        /// </summary>
        private void EnsureDirectory(MohClass mohClass)
        {
            var root = Path.GetFullPath(this.MohPath);

            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
                SetMode(root, AudioDirectoryMode);
            }

            var path = this.ClassPath(mohClass);

            if (Directory.Exists(path))
                return;

            Directory.CreateDirectory(path);
            SetMode(path, AudioDirectoryMode);
        }

        /// <summary>
        /// The last word on where a file may go. The pieces a path is built from are already an
        /// integer and two names matched against strict patterns, so this cannot fail today —
        /// which is the point of checking: the day someone assembles a path from something else,
        /// it fails here rather than in somebody's home directory.
        /// </summary>
        private string Inside(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(this.MohPath);

            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;

            if (!full.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidOperationException($"Refusing to touch '{full}': it is outside the music on hold directory.");

            return full;
        }

        /// <summary>
        /// Sets the mode where we can. A file the installer wrote may be owned by somebody else —
        /// the tracks that ship are (D122) — and a chmod needs ownership, so a refusal is logged
        /// rather than thrown: moving a file keeps its mode, so there was nothing to fix anyway.
        /// </summary>
        private static void SetMode(string path, UnixFileMode mode)
        {
            if (OperatingSystem.IsWindows())
                return;

            try
            {
                File.SetUnixFileMode(path, mode);
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Warn($"Could not set the mode of {path}: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes the upload to a temporary file, refusing anything over the cap or in a container
        /// we do not accept. A file, not memory, for the reason an announcement's upload is one:
        /// ffmpeg needs to seek, and tens of megabytes per request is not something to hold in the
        /// heap.
        /// </summary>
        private static AudioContainer Spool(Stream upload, string path)
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                var buffer = new byte[64 * 1024];
                long total = 0;
                int read;

                while ((read = upload.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > MaxUploadBytes)
                        throw new AudioUploadException($"That file is bigger than {MaxUploadBytes / (1024 * 1024)} MB.");

                    file.Write(buffer, 0, read);
                }

                if (total == 0)
                    throw new AudioUploadException("That file is empty.");
            }

            using var stored = File.OpenRead(path);
            var container = AudioSignature.Detect(stored);

            if (container == null)
                throw new AudioUploadException("That is not an audio file this system recognises. Use MP3, MP4/M4A, WAV, WebM or Ogg.");

            return container.Value;
        }
    }
}
