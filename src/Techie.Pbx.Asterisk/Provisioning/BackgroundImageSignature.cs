namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// What an uploaded background image actually is, read from its first bytes rather than from
    /// its name (D145, D152). A file called "logo.png" is whatever its contents say it is; the
    /// browser's file name and content type are never consulted.
    ///
    /// A gate, not a decoder, in the same spirit as <c>AudioSignature</c>: it answers "does this
    /// start the way a PNG or a JPEG starts", and the phone is what decides whether the rest of
    /// the file is any good. It is also what stands between an upload and the image decoder: a
    /// file that is to be resized (D154) has passed this first.
    /// </summary>
    public static class BackgroundImageSignature
    {
        /// <summary>How much of the file has to be read before the question can be answered: PNG's whole signature.</summary>
        public const int HeaderBytes = 8;

        /// <summary>
        /// What the file picker should offer. Advisory only — the browser's filter is a
        /// convenience, and <see cref="Detect(ReadOnlySpan{byte})"/> is what actually decides.
        /// </summary>
        public static string AcceptAttribute => ".png,.jpg,.jpeg,image/png,image/jpeg";

        /// <summary>The content type the image is served to a phone as.</summary>
        public static string ContentType(BackgroundImageFormat format) => format switch
        {
            BackgroundImageFormat.Png => "image/png",
            BackgroundImageFormat.Jpeg => "image/jpeg",
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        /// <summary>The format's usual name, for a message an admin reads.</summary>
        public static string Describe(BackgroundImageFormat format) => format switch
        {
            BackgroundImageFormat.Png => "PNG",
            BackgroundImageFormat.Jpeg => "JPEG",
            _ => format.ToString(),
        };

        /// <summary>
        /// The format this file is in, or null for anything we do not accept. Pure function over
        /// the first bytes, so it can be given a header read off a stream.
        /// </summary>
        public static BackgroundImageFormat? Detect(ReadOnlySpan<byte> header)
        {
            if (header.Length < HeaderBytes)
                return null;

            // The eight-byte PNG signature: \x89 P N G \r \n \x1A \n. All eight, because the
            // line-ending bytes are there precisely to catch a file that was mangled as text.
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
                return BackgroundImageFormat.Png;

            // A JPEG's start-of-image marker, then the first marker after it: FF D8 FF.
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return BackgroundImageFormat.Jpeg;

            return null;
        }

        /// <summary>
        /// Reads the header off the start of a seekable stream and leaves the position where it
        /// found it.
        /// </summary>
        public static BackgroundImageFormat? Detect(Stream stream)
        {
            if (!stream.CanSeek)
                throw new ArgumentException("The upload has to be seekable to be sniffed.", nameof(stream));

            var start = stream.Position;
            var header = new byte[HeaderBytes];
            var read = stream.ReadAtLeast(header, HeaderBytes, throwOnEndOfStream: false);
            stream.Position = start;

            return Detect(header.AsSpan(0, read));
        }

        /// <summary>The file name extension that goes with the format, on disk and in the URL a phone is given.</summary>
        public static string Extension(BackgroundImageFormat format) => format switch
        {
            BackgroundImageFormat.Png => ".png",
            BackgroundImageFormat.Jpeg => ".jpg",
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }
}
