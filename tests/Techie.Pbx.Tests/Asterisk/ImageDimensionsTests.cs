using Techie.Pbx.Asterisk.Provisioning;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The width and height an uploaded PNG or JPEG declares, read from its header (D153): a fixed
    /// place in a PNG, the start-of-frame segment of a JPEG, however many segments come first.
    /// </summary>
    public class ImageDimensionsTests
    {
        [Theory]
        [InlineData(320, 240)]
        [InlineData(800, 480)]
        [InlineData(60, 26)]
        [InlineData(182, 78)]
        [InlineData(70000, 1)]
        public void A_png_declares_its_size_in_ihdr(int width, int height)
        {
            Assert.Equal((width, height), ImageDimensions.Read(FakeImages.Png(width, height), BackgroundImageFormat.Png));
        }

        [Theory]
        [InlineData(320, 240)]
        [InlineData(800, 480)]
        [InlineData(60, 26)]
        [InlineData(182, 78)]
        [InlineData(65535, 1)]
        public void A_jpeg_declares_its_size_in_its_start_of_frame(int width, int height)
        {
            Assert.Equal((width, height), ImageDimensions.Read(FakeImages.Jpeg(width, height), BackgroundImageFormat.Jpeg));
        }

        /// <summary>
        /// Exif and quantisation tables before the frame, as a camera or an editor writes them:
        /// the walk skips each by its length field.
        /// </summary>
        [Fact]
        public void A_jpeg_frame_after_other_segments_is_found()
        {
            var file = new List<byte> { 0xFF, 0xD8 };

            // APP1 (Exif) with 300 bytes of payload, then a DQT with 65.
            file.AddRange(new byte[] { 0xFF, 0xE1, 0x01, 0x2E });
            file.AddRange(new byte[300]);
            file.AddRange(new byte[] { 0xFF, 0xDB, 0x00, 0x43 });
            file.AddRange(new byte[65]);
            file.AddRange(FakeImages.StartOfFrame(800, 480));

            Assert.Equal((800, 480), ImageDimensions.Read(file.ToArray(), BackgroundImageFormat.Jpeg));
        }

        /// <summary>Fill bytes before a marker are allowed by the format and skipped.</summary>
        [Fact]
        public void Jpeg_fill_bytes_before_a_marker_are_skipped()
        {
            var file = new List<byte> { 0xFF, 0xD8, 0xFF, 0xFF, 0xFF };
            file.AddRange(FakeImages.StartOfFrame(320, 240));

            Assert.Equal((320, 240), ImageDimensions.Read(file.ToArray(), BackgroundImageFormat.Jpeg));
        }

        /// <summary>
        /// Progressive (C2) and the other frame types carry the size in the same place. C4 is a
        /// Huffman table and not a frame, so it is skipped rather than read as one.
        /// </summary>
        [Fact]
        public void Every_start_of_frame_type_is_read_and_huffman_tables_are_not()
        {
            Assert.Equal((320, 240), ImageDimensions.Read(FakeImages.JpegHeader(320, 240, 0xC2), BackgroundImageFormat.Jpeg));

            var file = new List<byte> { 0xFF, 0xD8 };

            // A DHT whose first bytes, read as a frame header, would say 4660x22136.
            file.AddRange(new byte[] { 0xFF, 0xC4, 0x00, 0x08, 0x00, 0x12, 0x34, 0x56, 0x78, 0x00 });
            file.AddRange(FakeImages.StartOfFrame(182, 78));

            Assert.Equal((182, 78), ImageDimensions.Read(file.ToArray(), BackgroundImageFormat.Jpeg));
        }

        /// <summary>The compressed data starting before any frame header means there is no size to read.</summary>
        [Fact]
        public void A_jpeg_that_reaches_its_scan_before_a_frame_has_no_size()
        {
            var file = new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00 };

            Assert.Null(ImageDimensions.Read(file, BackgroundImageFormat.Jpeg));
        }

        /// <summary>A segment length that runs off the end of the file, or one under two, is corrupt.</summary>
        [Theory]
        [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x40, 0x00, 0x00, 0x00 })]
        [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x00, 0x00, 0x00 })]
        [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01, 0x00, 0x00 })]
        [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x04, 0x08, 0x00 })]
        [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
        [InlineData(new byte[] { 0xFF, 0xD8, 0x00, 0x00 })]
        public void A_malformed_jpeg_has_no_size(byte[] file)
        {
            Assert.Null(ImageDimensions.Read(file, BackgroundImageFormat.Jpeg));
        }

        /// <summary>The old test helper's shape: a JPEG signature and nothing but zeros after it.</summary>
        [Fact]
        public void A_jpeg_signature_followed_by_zeros_has_no_size()
        {
            var file = new byte[4096];
            new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.CopyTo(file, 0);

            Assert.Null(ImageDimensions.Read(file, BackgroundImageFormat.Jpeg));
        }

        [Fact]
        public void A_png_cut_off_before_its_height_has_no_size()
        {
            var file = FakeImages.Png(320, 240).AsSpan(0, 23);

            Assert.Null(ImageDimensions.Read(file, BackgroundImageFormat.Png));
        }

        /// <summary>IHDR must be the first chunk; anything else there is not a PNG we can read.</summary>
        [Fact]
        public void A_png_whose_first_chunk_is_not_ihdr_has_no_size()
        {
            var file = FakeImages.Png(320, 240);
            file[12] = (byte)'t';
            file[13] = (byte)'E';
            file[14] = (byte)'X';
            file[15] = (byte)'t';

            Assert.Null(ImageDimensions.Read(file, BackgroundImageFormat.Png));
        }

        /// <summary>A width with the top bit set is over PNG's own limit: a corrupt header, not a size.</summary>
        [Fact]
        public void A_png_width_over_the_format_limit_has_no_size()
        {
            var file = FakeImages.Png(320, 240);
            file[16] = 0x80;

            Assert.Null(ImageDimensions.Read(file, BackgroundImageFormat.Png));
        }

        [Fact]
        public void A_file_read_as_the_wrong_format_has_no_size()
        {
            Assert.Null(ImageDimensions.Read(FakeImages.Png(320, 240), BackgroundImageFormat.Jpeg));
            Assert.Null(ImageDimensions.Read(FakeImages.Jpeg(320, 240), BackgroundImageFormat.Png));
        }
    }
}
