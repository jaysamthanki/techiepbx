namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// PNG and JPEG files that are only headers: enough for the signature and the declared width
    /// and height to be read (D152, D153), padded with zeros to whatever size a test needs. Crafted
    /// here rather than checked in as binary fixtures, so what each test feeds in is readable.
    /// </summary>
    internal static class FakeImages
    {
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>
        /// A JFIF JPEG: start of image, an APP0 segment, then a baseline start-of-frame (C0)
        /// declaring the size. Nothing after it but zeros.
        /// </summary>
        public static byte[] Jpeg(int width, int height, int bytes = 4096) =>
            Padded(JpegHeader(width, height), bytes);

        /// <summary>The JPEG header alone, for tests that splice segments of their own in.</summary>
        public static byte[] JpegHeader(int width, int height, byte frameMarker = 0xC0)
        {
            var header = new List<byte> { 0xFF, 0xD8 };

            // APP0 "JFIF\0", version 1.1, no units, 1x1 density, no thumbnail: length 16.
            header.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 });
            header.AddRange(StartOfFrame(width, height, frameMarker));

            return header.ToArray();
        }

        /// <summary>
        /// A PNG: the signature, then an IHDR chunk declaring the size (8-bit RGB). The CRC is left
        /// as zeros, which nothing on our side checks.
        /// </summary>
        public static byte[] Png(int width, int height, int bytes = 4096)
        {
            var header = new List<byte>(PngSignature);

            header.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x0D, (byte)'I', (byte)'H', (byte)'D', (byte)'R' });
            header.AddRange(BigEndian32(width));
            header.AddRange(BigEndian32(height));
            header.AddRange(new byte[] { 0x08, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

            return Padded(header.ToArray(), bytes);
        }

        /// <summary>A start-of-frame segment: length 17, 8-bit precision, the size, three components.</summary>
        public static byte[] StartOfFrame(int width, int height, byte frameMarker = 0xC0) => new byte[]
        {
            0xFF, frameMarker, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width,
            0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        };

        private static byte[] BigEndian32(int value) =>
            new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };

        private static byte[] Padded(byte[] header, int bytes)
        {
            var content = new byte[Math.Max(bytes, header.Length)];
            header.CopyTo(content, 0);
            return content;
        }
    }
}
