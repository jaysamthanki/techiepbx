using log4net;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// What the site's Polycom background (D145, D151) and logo (D153) have in common: one image
    /// at a time, on disk in the app's data folder beside the database (D25). Not in wwwroot, which
    /// ships with the app and is readable by anyone; not in a table, because the file existing is
    /// the whole of the state. No image means no file, and no file means no line in any phone's
    /// config.
    ///
    /// Each image is stored under one of two fixed names — one per format — so no path is ever
    /// built from anything the browser sent. Saving a new image removes the other format's file,
    /// so the two can never both be there.
    ///
    /// The two stores differ only in the file name and the pixel sizes they accept, which is why
    /// this is a base class with no behaviour of its own to override rather than anything more.
    /// </summary>
    public abstract class PolycomImageStore
    {
        /// <summary>
        /// The cap on an upload (D152). The largest size either image may be is 800x480 (D153), and
        /// an image that size is a few hundred kilobytes; two megabytes leaves room for an
        /// unoptimised export without letting the phone be handed something it has to struggle
        /// to download on every boot.
        /// </summary>
        public const long MaxUploadBytes = 2L * 1024 * 1024;

        private static readonly ILog Log = LogManager.GetLogger(typeof(PolycomImageStore));

        private static readonly BackgroundImageFormat[] Formats = { BackgroundImageFormat.Png, BackgroundImageFormat.Jpeg };

        /// <summary>The pixel sizes an upload must be exactly one of (D153).</summary>
        private readonly (int Width, int Height)[] acceptedSizes;

        /// <summary>The stored file's name without its extension, which the format supplies.</summary>
        private readonly string fileStem;

        /// <summary>What the image is, for a log line or a message an admin reads: "background", "logo".</summary>
        private readonly string name;

        /// <summary>The data folder: the directory the database is in.</summary>
        public string DataDirectory { get; }

        protected PolycomImageStore(string dataDirectory, string fileStem, string name, params (int Width, int Height)[] acceptedSizes)
        {
            this.DataDirectory = Path.GetFullPath(dataDirectory);
            this.acceptedSizes = acceptedSizes;
            this.fileStem = fileStem;
            this.name = name;
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
                Log.Info($"Removed the Polycom {this.name} image");

            return removed;
        }

        /// <summary>Where an image of this format is stored. One fixed name per format, inside the data folder.</summary>
        public string PathFor(BackgroundImageFormat format) =>
            Path.Combine(this.DataDirectory, this.fileStem + BackgroundImageSignature.Extension(format));

        /// <summary>
        /// Takes an upload and makes it the site's image, replacing whatever was there.
        ///
        /// The upload is spooled to a temporary file in the data folder first, with the size cap
        /// enforced as it is read, and checked for a PNG or JPEG signature and for one of the
        /// accepted pixel sizes before it goes anywhere near the name a phone is served from.
        /// Anything refused leaves the current image completely untouched. The spool is in the
        /// same folder as the target so that putting it in place is a rename, and a phone fetching
        /// mid-save gets the old image or the new one, never half of either.
        /// </summary>
        public BackgroundImage Save(Stream upload)
        {
            Directory.CreateDirectory(this.DataDirectory);

            var spool = Path.Combine(this.DataDirectory, $"{this.fileStem}.upload-{Guid.NewGuid():N}");

            try
            {
                Spool(upload, spool);

                // At most the cap, so reading it whole is cheaper than being clever with a stream.
                var bytes = File.ReadAllBytes(spool);
                var format = this.Validate(bytes);
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
                Log.Info($"Polycom {this.name} image stored ({BackgroundImageSignature.Describe(format)}, {image.Bytes} bytes)");

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

        /// <summary>
        /// The upload's format, once it has been shown to be a PNG or JPEG of exactly one of the
        /// accepted sizes; otherwise a message saying which sizes those are.
        ///
        /// Exact, not "at most" or "roughly" (D153): these are site-wide images, one for a fleet of
        /// mixed models, and Poly's guides give one optimal size per screen class. An image that is
        /// any other size is one the phone rescales or crops, and what the admin sees on the desk is
        /// then not what they uploaded — so the size is checked here, where it can be explained,
        /// rather than discovered on a phone.
        /// </summary>
        private BackgroundImageFormat Validate(byte[] bytes)
        {
            var detected = BackgroundImageSignature.Detect(bytes);

            if (detected == null)
                throw new BackgroundUploadException("That is not a PNG or JPEG image. Only those two are accepted, and the file's contents decide, not its name.");

            var format = detected.Value;
            var accepted = string.Join(" or ", this.acceptedSizes.Select(s => $"{s.Width}x{s.Height}"));
            var size = ImageDimensions.Read(bytes, format);

            if (size == null)
                throw new BackgroundUploadException($"The width and height could not be read from that {BackgroundImageSignature.Describe(format)} file, so it cannot be checked against the {this.name} sizes ({accepted} pixels). It may be damaged.");

            if (!this.acceptedSizes.Contains(size.Value))
                throw new BackgroundUploadException($"The {this.name} image must be exactly {accepted} pixels; this file is {size.Value.Width}x{size.Value.Height}.");

            return format;
        }
    }
}
