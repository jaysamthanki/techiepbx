using System.Text;
using Techie.Pbx.Asterisk.Provisioning;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What an uploaded background image actually is, decided from its first bytes rather than
    /// from its name (D152). The name is whatever the browser says; the bytes are the file.
    /// </summary>
    public class BackgroundImageSignatureTests
    {
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>A header of the right length, with the given bytes at the front.</summary>
        private static byte[] Header(params byte[] start)
        {
            var header = new byte[16];
            start.CopyTo(header, 0);
            return header;
        }

        [Fact]
        public void A_png_is_its_eight_byte_signature()
        {
            Assert.Equal(BackgroundImageFormat.Png, BackgroundImageSignature.Detect(Header(PngSignature)));
        }

        /// <summary>JFIF, Exif and a bare JPEG all start with the start-of-image marker and another marker.</summary>
        [Theory]
        [InlineData((byte)0xE0)]
        [InlineData((byte)0xE1)]
        [InlineData((byte)0xDB)]
        public void A_jpeg_is_ff_d8_ff(byte marker)
        {
            Assert.Equal(BackgroundImageFormat.Jpeg, BackgroundImageSignature.Detect(Header(0xFF, 0xD8, 0xFF, marker)));
        }

        /// <summary>
        /// The case that matters: a text file somebody renamed to logo.png. The signature never sees
        /// the name, and the bytes are text.
        /// </summary>
        [Fact]
        public void A_text_file_renamed_png_is_refused()
        {
            var text = Encoding.ASCII.GetBytes("This is not an image, whatever the file is called.\n");

            Assert.Null(BackgroundImageSignature.Detect(text));

            using var stream = new MemoryStream(text);
            Assert.Null(BackgroundImageSignature.Detect(stream));
        }

        /// <summary>
        /// A PNG signature whose line-ending bytes were rewritten, which is what a file carried
        /// through a text-mode transfer looks like. PNG puts them there to catch exactly this.
        /// </summary>
        [Fact]
        public void A_png_mangled_as_text_is_refused()
        {
            Assert.Null(BackgroundImageSignature.Detect(Header(0x89, 0x50, 0x4E, 0x47, 0x0A, 0x1A, 0x0A, 0x00)));
        }

        [Theory]
        [InlineData("GIF89a")]
        [InlineData("BM")]
        [InlineData("RIFF")]
        [InlineData("<svg xmlns=")]
        public void Other_image_formats_are_refused(string start)
        {
            Assert.Null(BackgroundImageSignature.Detect(Header(Encoding.ASCII.GetBytes(start))));
        }

        [Fact]
        public void Too_short_to_tell_is_refused()
        {
            Assert.Null(BackgroundImageSignature.Detect(new byte[] { 0xFF, 0xD8, 0xFF }));
            Assert.Null(BackgroundImageSignature.Detect(ReadOnlySpan<byte>.Empty));
        }

        /// <summary>Sniffing a stream leaves it where it was, so the whole file can still be copied.</summary>
        [Fact]
        public void Detecting_from_a_stream_leaves_its_position_alone()
        {
            using var stream = new MemoryStream(Header(PngSignature));

            Assert.Equal(BackgroundImageFormat.Png, BackgroundImageSignature.Detect(stream));
            Assert.Equal(0, stream.Position);
        }
    }
}
