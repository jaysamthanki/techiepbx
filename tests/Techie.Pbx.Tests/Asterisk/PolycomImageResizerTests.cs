using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Techie.Pbx.Asterisk.Provisioning;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Making an upload of any size into the one size the site serves (D154): the background
    /// covers its target and is centre-cropped, the logo is contained in its target and padded
    /// with transparency, and either way the result is a PNG of exactly the target size.
    /// </summary>
    public class PolycomImageResizerTests
    {
        private static readonly (int Width, int Height) Background = (320, 240);
        private static readonly (int Width, int Height) Logo = (60, 26);

        private static void AssertColour(Rgba32 expected, Rgba32 actual)
        {
            // Resampling may move a channel by a step or two; a crop that kept the wrong band
            // moves it by nearly two hundred.
            Assert.InRange((int)actual.R, Math.Max(0, expected.R - 12), Math.Min(255, expected.R + 12));
            Assert.InRange((int)actual.G, Math.Max(0, expected.G - 12), Math.Min(255, expected.G + 12));
            Assert.InRange((int)actual.B, Math.Max(0, expected.B - 12), Math.Min(255, expected.B + 12));
            Assert.Equal((byte)255, actual.A);
        }

        [Theory]
        [InlineData(800, 480, PolycomImageFit.Cover)]
        [InlineData(1024, 600, PolycomImageFit.Cover)]
        [InlineData(32, 24, PolycomImageFit.Cover)]
        [InlineData(4000, 10, PolycomImageFit.Cover)]
        [InlineData(1, 3000, PolycomImageFit.Cover)]
        [InlineData(182, 78, PolycomImageFit.Contain)]
        [InlineData(3000, 1, PolycomImageFit.Contain)]
        [InlineData(1, 3000, PolycomImageFit.Contain)]
        public void Any_size_becomes_a_png_of_exactly_the_target(int width, int height, PolycomImageFit fit)
        {
            var target = fit == PolycomImageFit.Cover ? Background : Logo;

            foreach (var upload in new[] { EncodedImages.Png(width, height), EncodedImages.Jpeg(width, height) })
            {
                var output = PolycomImageResizer.Resize(upload, target, fit);

                Assert.Equal(BackgroundImageFormat.Png, BackgroundImageSignature.Detect(output));

                using var image = EncodedImages.Decode(output);
                Assert.Equal(target, (image.Width, image.Height));
            }
        }

        /// <summary>Wider than 4:3: the sides are cut, and the middle fills the whole screen.</summary>
        [Fact]
        public void A_wide_background_loses_its_sides()
        {
            var upload = EncodedImages.Banded(400, 240, band: 40, vertical: false, outer: EncodedImages.Red, inner: EncodedImages.Green);

            using var image = EncodedImages.Decode(PolycomImageResizer.Resize(upload, Background, PolycomImageFit.Cover));

            AssertColour(EncodedImages.Green, image[0, 120]);
            AssertColour(EncodedImages.Green, image[160, 120]);
            AssertColour(EncodedImages.Green, image[319, 120]);
        }

        /// <summary>Taller than 4:3: the top and bottom are cut instead.</summary>
        [Fact]
        public void A_tall_background_loses_its_top_and_bottom()
        {
            var upload = EncodedImages.Banded(320, 480, band: 120, vertical: true, outer: EncodedImages.Red, inner: EncodedImages.Green);

            using var image = EncodedImages.Decode(PolycomImageResizer.Resize(upload, Background, PolycomImageFit.Cover));

            AssertColour(EncodedImages.Green, image[160, 0]);
            AssertColour(EncodedImages.Green, image[160, 120]);
            AssertColour(EncodedImages.Green, image[160, 239]);
        }

        /// <summary>
        /// A logo wider than 60:26 is scaled to the full width and padded above and below with
        /// transparency — its ends are still there, not cropped.
        /// </summary>
        [Fact]
        public void A_wide_logo_is_padded_above_and_below_not_cropped()
        {
            using var image = EncodedImages.Decode(PolycomImageResizer.Resize(EncodedImages.Png(120, 26), Logo, PolycomImageFit.Contain));

            Assert.Equal((byte)0, image[30, 0].A);
            Assert.Equal((byte)0, image[30, 25].A);
            AssertColour(EncodedImages.Blue, image[0, 12]);
            AssertColour(EncodedImages.Blue, image[30, 12]);
            AssertColour(EncodedImages.Blue, image[59, 12]);
        }

        /// <summary>A logo narrower than 60:26 is scaled to the full height and padded either side.</summary>
        [Fact]
        public void A_tall_logo_is_padded_either_side_not_cropped()
        {
            using var image = EncodedImages.Decode(PolycomImageResizer.Resize(EncodedImages.Jpeg(26, 260), Logo, PolycomImageFit.Contain));

            Assert.Equal((byte)0, image[0, 13].A);
            Assert.Equal((byte)0, image[59, 13].A);
            Assert.Equal((byte)255, image[29, 0].A);
            Assert.Equal((byte)255, image[29, 13].A);
            Assert.Equal((byte)255, image[29, 25].A);
        }

        /// <summary>
        /// A file that starts like a PNG but is only a header cannot be decoded, so it cannot be
        /// resized: refused with a message, not an ImageSharp exception.
        /// </summary>
        [Fact]
        public void A_file_that_cannot_be_decoded_is_refused()
        {
            var refused = Assert.Throws<BackgroundUploadException>(() =>
                PolycomImageResizer.Resize(FakeImages.Png(800, 480), Background, PolycomImageFit.Cover));

            Assert.Contains("could not be read", refused.Message);
        }

        /// <summary>
        /// A small file can declare an enormous image. Past the cap it is refused on its header,
        /// before the pixels are decoded.
        /// </summary>
        [Fact]
        public void An_image_over_the_pixel_cap_is_refused()
        {
            byte[] upload;

            using (var huge = new Image<L8>(6000, 5000))
            using (var stream = new MemoryStream())
            {
                huge.SaveAsPng(stream);
                upload = stream.ToArray();
            }

            Assert.True(upload.Length < PolycomImageStore.MaxUploadBytes);

            var refused = Assert.Throws<BackgroundUploadException>(() =>
                PolycomImageResizer.Resize(upload, Background, PolycomImageFit.Cover));

            Assert.Contains("6000x5000", refused.Message);
        }
    }
}
