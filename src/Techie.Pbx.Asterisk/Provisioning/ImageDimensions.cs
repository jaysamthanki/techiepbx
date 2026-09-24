namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// How many pixels wide and high an uploaded PNG or JPEG says it is, read from its header
    /// (D153). Still a gate, not a decoder, like <see cref="BackgroundImageSignature"/>: it reads
    /// the two numbers the file declares and nothing else. Since D154 it decides whether an upload
    /// is already the target size and can be stored untouched, without the file being decoded;
    /// anything else goes to <see cref="PolycomImageResizer"/>, which does decode it.
    ///
    /// PNG puts them at a fixed place: the first chunk after the signature must be IHDR, and its
    /// data starts with the width and then the height, four bytes each, big-endian.
    ///
    /// JPEG has no fixed place: the numbers are in the start-of-frame segment, which comes after
    /// however many other segments (JFIF, Exif, quantisation tables) the encoder wrote first. So
    /// the segments are walked by their length fields until a start-of-frame turns up, and a file
    /// that reaches the image data or runs off its end first has no size we can read.
    /// </summary>
    public static class ImageDimensions
    {
        /// <summary>
        /// The width and height the file declares, or null when the header is missing, truncated or
        /// malformed. Pure function over the file's bytes.
        /// </summary>
        public static (int Width, int Height)? Read(ReadOnlySpan<byte> file, BackgroundImageFormat format) => format switch
        {
            BackgroundImageFormat.Png => ReadPng(file),
            BackgroundImageFormat.Jpeg => ReadJpeg(file),
            _ => null,
        };

        private static int BigEndian16(ReadOnlySpan<byte> bytes, int offset) =>
            (bytes[offset] << 8) | bytes[offset + 1];

        private static long BigEndian32(ReadOnlySpan<byte> bytes, int offset) =>
            ((long)bytes[offset] << 24) | ((long)bytes[offset + 1] << 16) | ((long)bytes[offset + 2] << 8) | bytes[offset + 3];

        /// <summary>
        /// A start-of-frame marker: C0 to CF, less the three in that range that are something
        /// else — C4 (Huffman tables), C8 (reserved) and CC (arithmetic coding conditioning).
        /// </summary>
        private static bool IsStartOfFrame(byte marker) =>
            marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;

        private static (int Width, int Height)? ReadJpeg(ReadOnlySpan<byte> file)
        {
            if (file.Length < 4 || file[0] != 0xFF || file[1] != 0xD8)
                return null;

            var position = 2;

            while (position < file.Length)
            {
                if (file[position] != 0xFF)
                    return null;

                // Any number of FF fill bytes may come before the marker itself.
                while (position < file.Length && file[position] == 0xFF)
                    position++;

                if (position >= file.Length)
                    return null;

                var marker = file[position];
                position++;

                // Markers that stand alone, with no length after them: TEM, the restart markers and
                // a repeated start of image.
                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD8))
                    continue;

                // End of image, or the start of the compressed data, before any frame header.
                if (marker == 0xD9 || marker == 0xDA)
                    return null;

                if (position + 2 > file.Length)
                    return null;

                // The segment length counts its own two bytes, so anything under two is corrupt.
                var length = BigEndian16(file, position);
                if (length < 2 || position + length > file.Length)
                    return null;

                if (IsStartOfFrame(marker))
                {
                    // Length (2), sample precision (1), then height (2) and width (2).
                    if (length < 7)
                        return null;

                    var height = BigEndian16(file, position + 3);
                    var width = BigEndian16(file, position + 5);

                    return (width, height);
                }

                position += length;
            }

            return null;
        }

        private static (int Width, int Height)? ReadPng(ReadOnlySpan<byte> file)
        {
            // Signature (8), IHDR's length (4) and type (4), width (4), height (4).
            if (file.Length < 24)
                return null;

            if (file[12] != (byte)'I' || file[13] != (byte)'H' || file[14] != (byte)'D' || file[15] != (byte)'R')
                return null;

            var width = BigEndian32(file, 16);
            var height = BigEndian32(file, 20);

            // PNG limits both to 2^31 - 1; anything bigger is a corrupt header, not a big image.
            if (width > int.MaxValue || height > int.MaxValue)
                return null;

            return ((int)width, (int)height);
        }
    }
}
