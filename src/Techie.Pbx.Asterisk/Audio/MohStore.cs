using log4net;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Audio
{
    /// <summary>
    /// The music on hold audio on disk: one flat directory, one converted WAV per track (D119).
    /// The web user writes these files; the <c>asterisk</c> user reads them through its group,
    /// exactly as it reads the announcements and the generated conf files (D18, D55).
    ///
    /// One directory rather than one per track, because <c>res_musiconhold</c> in <c>files</c>
    /// mode plays a directory: the class names it and Asterisk reads everything in it. That is
    /// also why the path is a constant rather than a setting — the generated musiconhold.conf
    /// names it, so moving it in one place only would leave Asterisk looking somewhere else.
    ///
    /// Every path this class produces is built from a row ID and a file name it derived itself,
    /// and is then checked to be inside the base directory before anything is written or deleted.
    /// Nothing from the browser — least of all the uploaded file name — ever reaches a path.
    /// </summary>
    public class MohStore
    {
        /// <summary>
        /// Where the tracks go. The same path the generated musiconhold.conf names as the class's
        /// directory, and the one the installer creates setgid asterisk (D119).
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
        /// group. The installer creates this directory; this mode is only used when it has not,
        /// which is a development box.
        /// </summary>
        private const UnixFileMode AudioDirectoryMode =
            UnixFileMode.SetGroup |
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute;

        private static readonly ILog Log = LogManager.GetLogger(typeof(MohStore));

        private readonly AudioConverter converter;

        /// <summary>The base directory. Everything this class writes is under it.</summary>
        public string MohPath { get; }

        public MohStore()
            : this(DefaultMohPath, new AudioConverter())
        {
        }

        public MohStore(string mohPath)
            : this(mohPath, new AudioConverter())
        {
        }

        public MohStore(string mohPath, AudioConverter converter)
        {
            this.MohPath = string.IsNullOrWhiteSpace(mohPath) ? DefaultMohPath : mohPath.Trim();
            this.converter = converter;
        }

        /// <summary>
        /// Removes one track's file. Called when the row is deleted, so that deleting a track does
        /// not leave music playing to parked callers for ever.
        /// </summary>
        public void Delete(MohFile file)
        {
            if (!file.HasAudio || !MohFile.IsValidFile(file.File))
                return;

            var path = this.PathFor(file.MohFileID, file.File);

            if (!File.Exists(path))
                return;

            File.Delete(path);
            Log.Info($"Removed the music on hold file for track {file.MohFileID}");
        }

        /// <summary>
        /// What is on disk for this track, or null when the row names a file that is not there. A
        /// row and a file can drift apart — a failed write, a restored backup, a hand edit — and
        /// the UI says so rather than showing a length for nothing.
        /// </summary>
        public AnnouncementAudio? Describe(MohFile file)
        {
            if (!file.HasAudio || !MohFile.IsValidFile(file.File))
                return null;

            var path = this.PathFor(file.MohFileID, file.File);
            if (!File.Exists(path))
                return null;

            return new AnnouncementAudio
            {
                Bytes = new FileInfo(path).Length,
                FileName = file.File,
            };
        }

        /// <summary>The full path of one track's audio, checked to be inside the base path.</summary>
        public string PathFor(long mohFileID, string fileName)
        {
            if (mohFileID <= 0)
                throw new InvalidOperationException("A music on hold track has to be saved before its audio can be.");

            if (!MohFile.IsValidFile(fileName))
                throw new InvalidOperationException($"Refusing to use '{fileName}' as a music on hold file name.");

            return this.Inside(Path.Combine(Path.GetFullPath(this.MohPath), fileName));
        }

        /// <summary>
        /// The track's existing audio under a new name, for when the track itself was renamed. The
        /// file is named after the track, so a rename that left the old file there would leave the
        /// directory holding music nobody could explain — and Asterisk would play both.
        ///
        /// Both names are taken as arguments rather than read off the row, because during a rename
        /// the row already says the new one and the disk still says the old.
        /// </summary>
        public void Rename(long mohFileID, string fromFileName, string toFileName)
        {
            var from = this.PathFor(mohFileID, fromFileName);
            var to = this.PathFor(mohFileID, toFileName);

            if (string.Equals(from, to, StringComparison.Ordinal) || !File.Exists(from))
                return;

            File.Move(from, to, overwrite: true);
            SetMode(to, AudioFileMode);

            Log.Info($"Music on hold track {mohFileID} renamed to {toFileName}");
        }

        /// <summary>
        /// Takes an upload, converts it, and makes it the track's audio. Returns the file name that
        /// was stored, which is derived from the track's ID and name and never from the browser's.
        ///
        /// The conversion happens in a temporary directory first, so an upload that ffmpeg refuses
        /// — or an ffmpeg that is not installed — leaves whatever the track already had completely
        /// untouched.
        /// </summary>
        public string Save(MohFile file, Stream upload)
        {
            var fileName = MohFile.FileNameFor(file.MohFileID, file.Name);
            var target = this.PathFor(file.MohFileID, fileName);
            var work = Directory.CreateTempSubdirectory("tnpbx-moh-");

            try
            {
                var source = Path.Combine(work.FullName, "upload");
                var converted = Path.Combine(work.FullName, fileName);

                var container = Spool(upload, source);
                this.converter.ToPrompt(source, converted);

                this.EnsureDirectory();

                File.Move(converted, target, overwrite: true);
                SetMode(target, AudioFileMode);

                // One track, one file. Replacing the audio, or renaming the track in the same
                // save, must not leave a second copy in a directory Asterisk plays whole.
                foreach (var other in Directory.EnumerateFiles(Path.GetFullPath(this.MohPath), $"{file.MohFileID}-*"))
                {
                    if (!string.Equals(other, target, StringComparison.Ordinal))
                        File.Delete(other);
                }

                Log.Info($"Music on hold track {file.MohFileID} stored as {fileName} " +
                         $"(from {AudioSignature.Describe(container)}, {new FileInfo(target).Length} bytes)");

                return fileName;
            }
            finally
            {
                work.Delete(recursive: true);
            }
        }

        /// <summary>
        /// Creates the directory if it is not there, and sets the mode only when it had to create
        /// it. On a real install the installer made it, owned <c>asterisk:asterisk</c> and setgid
        /// so that what the web user writes is group-readable by Asterisk (D119), and taking a
        /// chmod to it would undo that.
        /// </summary>
        private void EnsureDirectory()
        {
            var root = Path.GetFullPath(this.MohPath);

            if (Directory.Exists(root))
                return;

            Directory.CreateDirectory(root);
            SetMode(root, AudioDirectoryMode);
        }

        /// <summary>
        /// The last word on where a file may go. The pieces a path is built from are already an
        /// integer and a name matched against a strict pattern, so this cannot fail today — which
        /// is the point of checking: the day someone assembles a path from something else, it
        /// fails here rather than in somebody's home directory.
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

        private static void SetMode(string path, UnixFileMode mode)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, mode);
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
