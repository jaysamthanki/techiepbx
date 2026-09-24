using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Real, decodable PNG and JPEG files of any size, built with ImageSharp (D154). Where
    /// <see cref="FakeImages"/> is only a header — enough for the signature and the declared size
    /// — these are what an upload that has to be resized must be, because resizing decodes it.
    /// </summary>
    internal static class EncodedImages
    {
        public static readonly Rgba32 Blue = new(30, 60, 220);
        public static readonly Rgba32 Green = new(20, 200, 40);
        public static readonly Rgba32 Red = new(220, 30, 30);

        /// <summary>The bytes a PNG or JPEG decodes to, for a test to look at the pixels.</summary>
        public static Image<Rgba32> Decode(byte[] file) => Image.Load<Rgba32>(file);

        /// <summary>A JPEG of one solid colour.</summary>
        public static byte[] Jpeg(int width, int height) => Encode(Solid(width, height), jpeg: true);

        /// <summary>A PNG of one solid colour.</summary>
        public static byte[] Png(int width, int height) => Encode(Solid(width, height), jpeg: false);

        /// <summary>
        /// A PNG that is <paramref name="inner"/> in the middle and <paramref name="outer"/> in a
        /// band <paramref name="band"/> pixels wide down each side (or across the top and bottom,
        /// when <paramref name="vertical"/>) — so a test can see exactly what a crop kept.
        /// </summary>
        public static byte[] Banded(int width, int height, int band, bool vertical, Rgba32 outer, Rgba32 inner)
        {
            var image = new Image<Rgba32>(width, height, inner);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var position = vertical ? y : x;
                    var length = vertical ? height : width;

                    if (position < band || position >= length - band)
                        image[x, y] = outer;
                }
            }

            return Encode(image, jpeg: false);
        }

        private static byte[] Encode(Image<Rgba32> image, bool jpeg)
        {
            using (image)
            {
                using var stream = new MemoryStream();

                if (jpeg)
                    image.SaveAsJpeg(stream);
                else
                    image.SaveAsPng(stream);

                return stream.ToArray();
            }
        }

        private static Image<Rgba32> Solid(int width, int height) => new(width, height, Blue);
    }
}
