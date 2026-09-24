using log4net;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// The one site-wide Polycom background image, on disk in the app's data folder beside the
    /// database (D25, D145, D151). Not in wwwroot, which ships with the app and is readable by
    /// anyone; not in a table, because the file existing is the whole of the state. No image means
    /// no file, and no file means no <c>bg</c> block in any phone's config.
    ///
    /// There is at most one image at a time, stored under one of two fixed names — one per format
    /// — so no path is ever built from anything the browser sent. Saving a new image removes the
    /// other format's file, so the two can never both be there.
    /// </summary>
    public class BackgroundStore
    {
        /// <summary>
        /// The cap on an upload (D152). The largest Poly screen a background is drawn on is about
        /// 1024x600, and an image that size is a few hundred kilobytes; two megabytes leaves room
        /// for an unoptimised export without letting the phone be handed something it has to
        /// struggle to download on every boot.
        /// </summary>
        public const long MaxUploadBytes = 2L * 1024 * 1024;

        /// <summary>The stored file's name without its extension, which the format supplies.</summary>
        private const string FileStem = "polycom-background";

        private static readonly ILog Log = LogManager.GetLogger(typeof(BackgroundStore));

        private static readonly BackgroundImageFormat[] Formats = { BackgroundImageFormat.Png, BackgroundImageFormat.Jpeg };

        /// <summary>The data folder: the directory the database is in.</summary>
        public string DataDirectory { get; }

        public BackgroundStore(Database database)
            : this(Path.GetDirectoryName(Path.GetFullPath(database.FilePath))!)
        {
        }

        public BackgroundStore(string dataDirectory)
        {
            this.DataDirectory = Path.GetFullPath(dataDirectory);
        }

        /// <summary>The image there is now, or null when none has been uploaded or it was removed.</summary>
        public BackgroundImage? Current()
        {
            foreach (var format in Formats)
            {
                var path = this.PathFor(format);

                if (File.Exists(path))
                    return new BackgroundImage { Bytes = new FileInfo(path).Length, Format = format, Path = path };
            }

            return null;
        }

        /// <summary>Removes the image, whichever format it is in. Returns whether there was one.</summary>
        public bool Delete()
        {
            var removed = false;

            foreach (var format in Formats)
            {
                var path = this.PathFor(format);

                if (!File.Exists(path))
                    continue;

                File.Delete(path);
                removed = true;
            }

            if (removed)
                Log.Info("Removed the Polycom background image");

            return removed;
        }

        /// <summary>Where an image of this format is stored. One fixed name per format, inside the data folder.</summary>
        public string PathFor(BackgroundImageFormat format) =>
            Path.Combine(this.DataDirectory, FileStem + BackgroundImageSignature.Extension(format));

        /// <summary>
        /// Takes an upload and makes it the site's background image, replacing whatever was there.
        ///
        /// The upload is spooled to a temporary file in the data folder first, with the size cap
        /// enforced as it is read, and checked for a PNG or JPEG signature before it goes anywhere
        /// near the name a phone is served from. Anything refused leaves the current image
        /// completely untouched. The spool is in the same folder as the target so that putting it
        /// in place is a rename, and a phone fetching mid-save gets the old image or the new one,
        /// never half of either.
        /// </summary>
        public BackgroundImage Save(Stream upload)
        {
            Directory.CreateDirectory(this.DataDirectory);

            var spool = Path.Combine(this.DataDirectory, $"{FileStem}.upload-{Guid.NewGuid():N}");

            try
            {
                Spool(upload, spool);

                BackgroundImageFormat? detected;
                using (var stored = File.OpenRead(spool))
                    detected = BackgroundImageSignature.Detect(stored);

                if (detected == null)
                    throw new BackgroundUploadException("That is not a PNG or JPEG image. Only those two are accepted, and the file's contents decide, not its name.");

                var format = detected.Value;
                var target = this.PathFor(format);

                File.Move(spool, target, overwrite: true);

                // One image at a time: a PNG replacing a JPEG must not leave the JPEG behind for
                // Current() to find first.
                foreach (var other in Formats.Where(f => f != format))
                {
                    var stale = this.PathFor(other);
                    if (File.Exists(stale))
                        File.Delete(stale);
                }

                var image = new BackgroundImage { Bytes = new FileInfo(target).Length, Format = format, Path = target };
                Log.Info($"Polycom background image stored ({BackgroundImageSignature.Describe(format)}, {image.Bytes} bytes)");

                return image;
            }
            finally
            {
                if (File.Exists(spool))
                    File.Delete(spool);
            }
        }

        /// <summary>
        /// Copies the upload to a file, refusing it the moment it passes the cap rather than after
        /// the whole thing has been written.
        /// </summary>
        private static void Spool(Stream upload, string path)
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);

            var buffer = new byte[64 * 1024];
            long total = 0;
            int read;

            while ((read = upload.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > MaxUploadBytes)
                    throw new BackgroundUploadException($"That image is bigger than {MaxUploadBytes / (1024 * 1024)} MB.");

                file.Write(buffer, 0, read);
            }

            if (total == 0)
                throw new BackgroundUploadException("That file is empty.");
        }
    }
}
