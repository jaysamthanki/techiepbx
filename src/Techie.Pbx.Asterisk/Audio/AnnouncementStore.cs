using System.Globalization;
using log4net;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Audio
{
    /// <summary>
    /// The announcement audio on disk: one directory per announcement, one converted WAV in it
    /// (D55). The web user writes these files; the <c>asterisk</c> user reads them through its
    /// group, exactly as it reads the generated conf files (D18).
    ///
    /// Every path this class produces is built from an announcement ID and a file name it derived
    /// itself, and is then checked to be inside the configured base directory before anything is
    /// written or deleted. Nothing from the browser — least of all the uploaded file name — ever
    /// reaches a path.
    /// </summary>
    public class AnnouncementStore
    {
        /// <summary>Where the files go when configuration says nothing.</summary>
        public const string DefaultSoundsPath = "/var/lib/asterisk/sounds/tnpbx";

        /// <summary>
        /// What Playback() is given, before the announcement's own directory and file. Asterisk
        /// resolves a relative prompt name against its own sounds directory, so this is the base
        /// path's last element plus ours. Moving <see cref="SoundsPath"/> outside
        /// <c>&lt;asterisk sounds&gt;/tnpbx</c> would leave Asterisk unable to find the file (D55).
        /// </summary>
        public const string DialplanPrefix = "tnpbx/" + SubDirectory;

        /// <summary>
        /// The cap on an upload, before conversion. A prompt is seconds long; twenty megabytes is
        /// already a generous video, and refusing early is what keeps a large file from being
        /// spooled and handed to ffmpeg at all.
        /// </summary>
        public const long MaxUploadBytes = 20L * 1024 * 1024;

        /// <summary>The one directory under the base path that this feature owns.</summary>
        public const string SubDirectory = "announcements";

        /// <summary>Owner read/write, group read: the asterisk user reads through its group (D18).</summary>
        private const UnixFileMode AudioFileMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

        /// <summary>
        /// The same, plus the execute bits a directory needs to be entered, plus setgid. Without
        /// setgid, a file created inside gets the web user's primary group (techie), not the
        /// directory's asterisk group — the /etc/asterisk trick (D18) works because the conf writer
        /// only writes into the setgid base, and this store creates directories itself, so the bit
        /// has to be carried along. Linux propagates a directory's setgid to its files' group and
        /// to its subdirectories' setgid, so one bit fixes every level below.
        /// </summary>
        private const UnixFileMode AudioDirectoryMode =
            UnixFileMode.SetGroup |
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute;

        private static readonly ILog Log = LogManager.GetLogger(typeof(AnnouncementStore));

        private readonly AudioConverter converter;

        /// <summary>The base directory, as configured. Everything this class writes is under it.</summary>
        public string SoundsPath { get; }

        public AnnouncementStore(string soundsPath)
            : this(soundsPath, new AudioConverter())
        {
        }

        public AnnouncementStore(string soundsPath, AudioConverter converter)
        {
            this.SoundsPath = string.IsNullOrWhiteSpace(soundsPath) ? DefaultSoundsPath : soundsPath.Trim();
            this.converter = converter;
        }

        /// <summary>
        /// What Playback() is given for an announcement: no extension, so Asterisk picks the best
        /// format it has — which is the only one we wrote.
        /// </summary>
        public static string PlaybackName(Announcement announcement) =>
            $"{DialplanPrefix}/{announcement.AnnouncementID.ToString(CultureInfo.InvariantCulture)}/{announcement.AudioBaseName}";

        /// <summary>
        /// Removes an announcement's whole directory. Called when the announcement is deleted, so
        /// that deleting the row does not leave audio behind for ever.
        /// </summary>
        public void Delete(Announcement announcement)
        {
            var directory = this.DirectoryFor(announcement.AnnouncementID);

            if (!Directory.Exists(directory))
                return;

            Directory.Delete(directory, recursive: true);
            Log.Info($"Removed the audio directory for announcement {announcement.AnnouncementID}");
        }

        /// <summary>
        /// What is on disk for this announcement, or null when the row names a file that is not
        /// there. A row and a file can drift apart — a failed write, a restored backup, a hand
        /// edit — and the UI says so rather than showing a length for nothing.
        /// </summary>
        public AnnouncementAudio? Describe(Announcement announcement)
        {
            if (!announcement.HasAudio || !Announcement.IsValidAudioFile(announcement.AudioFile))
                return null;

            var path = this.PathFor(announcement.AnnouncementID, announcement.AudioFile);
            if (!File.Exists(path))
                return null;

            return new AnnouncementAudio
            {
                Bytes = new FileInfo(path).Length,
                FileName = announcement.AudioFile,
            };
        }

        /// <summary>The full path of an announcement's audio, checked to be inside the base path.</summary>
        public string PathFor(long announcementID, string fileName)
        {
            if (announcementID <= 0)
                throw new InvalidOperationException("An announcement has to be saved before its audio can be.");

            if (!Announcement.IsValidAudioFile(fileName))
                throw new InvalidOperationException($"Refusing to use '{fileName}' as an audio file name.");

            return this.Inside(Path.Combine(this.DirectoryFor(announcementID), fileName));
        }

        /// <summary>
        /// The announcement's existing audio under a new name, for when the announcement itself
        /// was renamed. The file is named after the announcement, so a rename that left the old
        /// file there would leave the directory holding a file nobody could explain.
        ///
        /// Both names are taken as arguments rather than read off the row, because during a rename
        /// the row already says the new one and the disk still says the old.
        /// </summary>
        public void Rename(long announcementID, string fromFileName, string toFileName)
        {
            var from = this.PathFor(announcementID, fromFileName);
            var to = this.PathFor(announcementID, toFileName);

            if (string.Equals(from, to, StringComparison.Ordinal) || !File.Exists(from))
                return;

            File.Move(from, to, overwrite: true);
            SetMode(to, AudioFileMode);

            Log.Info($"Announcement {announcementID} audio renamed to {toFileName}");
        }

        /// <summary>
        /// Takes an upload, converts it, and makes it the announcement's audio. Returns the file
        /// name that was stored, which is derived from the announcement's name and never from the
        /// browser's.
        ///
        /// The conversion happens in a temporary directory first, so an upload that ffmpeg refuses
        /// — or an ffmpeg that is not installed — leaves whatever the announcement already had
        /// completely untouched.
        /// </summary>
        public string Save(Announcement announcement, Stream upload)
        {
            var fileName = Announcement.FileNameFor(announcement.Name);
            var target = this.PathFor(announcement.AnnouncementID, fileName);
            var work = Directory.CreateTempSubdirectory("tnpbx-audio-");

            try
            {
                var source = Path.Combine(work.FullName, "upload");
                var converted = Path.Combine(work.FullName, fileName);

                var container = Spool(upload, source);
                this.converter.ToPrompt(source, converted);

                var directory = this.EnsureDirectories(announcement.AnnouncementID);

                File.Move(converted, target, overwrite: true);
                SetMode(target, AudioFileMode);

                // One announcement, one file: replacing the audio, or renaming the announcement in
                // the same save, must not leave the old recording playable.
                foreach (var other in Directory.EnumerateFiles(directory))
                {
                    if (!string.Equals(other, target, StringComparison.Ordinal))
                        File.Delete(other);
                }

                Log.Info($"Announcement {announcement.AnnouncementID} audio stored as {fileName} " +
                         $"(from {AudioSignature.Describe(container)}, {new FileInfo(target).Length} bytes)");

                return fileName;
            }
            finally
            {
                work.Delete(recursive: true);
            }
        }

        /// <summary>One announcement's directory, checked to be inside the base path.</summary>
        private string DirectoryFor(long announcementID) =>
            this.Inside(Path.Combine(
                this.SoundsPath, SubDirectory, announcementID.ToString(CultureInfo.InvariantCulture)));

        /// <summary>
        /// Creates the three levels — base, "announcements", the announcement's own — and sets the
        /// mode on each one it had to create. A directory that is already there is left alone: on
        /// a real install the base is the installer's, owned <c>root:asterisk</c> and setgid so
        /// that what we create under it is group-owned by asterisk (D55), and taking a chmod to it
        /// would undo that.
        /// </summary>
        private string EnsureDirectories(long announcementID)
        {
            var root = Path.GetFullPath(this.SoundsPath);

            Create(root);
            Create(Path.Combine(root, SubDirectory));

            var directory = this.DirectoryFor(announcementID);
            Create(directory);

            return directory;

            static void Create(string path)
            {
                if (Directory.Exists(path))
                    return;

                Directory.CreateDirectory(path);
                SetMode(path, AudioDirectoryMode);
            }
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
            var root = Path.GetFullPath(this.SoundsPath);

            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;

            if (!full.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidOperationException($"Refusing to touch '{full}': it is outside the sounds directory.");

            return full;
        }

        private static void SetMode(string path, UnixFileMode mode)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, mode);
        }

        /// <summary>
        /// Writes the upload to a temporary file, refusing anything over the cap or in a container
        /// we do not accept. A file, not memory: an MP4's index can be at the end of it, so ffmpeg
        /// needs to be able to seek, and twenty megabytes per request is not something to hold in
        /// the heap either.
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
                        throw new AudioUploadException($"That file is bigger than {MaxUploadBytes / (1024 * 1024)} MB. An announcement should be seconds long.");

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
