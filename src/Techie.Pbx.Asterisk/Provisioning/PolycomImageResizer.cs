using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// Makes an uploaded background or logo into the one size the site serves (D154), and always
    /// a PNG. A pure function, bytes in and bytes out; <see cref="PolycomImageStore"/> decides when
    /// it is needed, which is only when the upload is not already exactly the target size.
    ///
    /// SixLabors.ImageSharp does the decoding and resampling, the project's one image package:
    /// hand-rolled PNG/JPEG decoding and resampling would be more code, and more risk, than a
    /// maintained library. It is given a configuration that knows PNG and JPEG and nothing else,
    /// so the rest of its decoders are unreachable from an upload whatever the magic-byte check
    /// lets through.
    /// </summary>
    public static class PolycomImageResizer
    {
        /// <summary>
        /// The most pixels an upload may declare before it is decoded (D154). The cap on the file
        /// (<see cref="PolycomImageStore.MaxUploadBytes"/>) does not bound what it decodes to — a
        /// small PNG can declare an enormous canvas — and a decoded pixel is four bytes here. 25
        /// megapixels (100 MB decoded) is past a 24-megapixel camera photo, far more than a phone
        /// screen needs.
        /// </summary>
        public const long MaxSourcePixels = 25_000_000;

        /// <summary>PNG and JPEG only: what the magic-byte check allows (D152), and nothing else.</summary>
        private static readonly Configuration Decoders = new(new PngConfigurationModule(), new JpegConfigurationModule());

        private static readonly DecoderOptions Options = new() { Configuration = Decoders };

        /// <summary>
        /// Scaled by the one factor that fits both sides inside the target, then padded evenly with
        /// transparent pixels to exactly the target. Never cropped and never stretched.
        /// </summary>
        private static void Contain(Image<Rgba32> image, (int Width, int Height) target)
        {
            var scale = Math.Min((double)target.Width / image.Width, (double)target.Height / image.Height);
            var width = Math.Clamp((int)Math.Round(image.Width * scale), 1, target.Width);
            var height = Math.Clamp((int)Math.Round(image.Height * scale), 1, target.Height);

            image.Mutate(context => context
                .Resize(new ResizeOptions { Mode = ResizeMode.Stretch, Size = new Size(width, height) })
                .Pad(target.Width, target.Height, Color.Transparent));
        }

        /// <summary>
        /// The same aspect ratio as the target, as much of it as fits, cut from the middle of the
        /// image; then scaled to the target. Cropping first, in the source's own pixels, means the
        /// scale never has to produce anything bigger than the target, however extreme the
        /// upload's shape.
        /// </summary>
        private static void Cover(Image<Rgba32> image, (int Width, int Height) target)
        {
            long width = image.Width;
            long height = image.Height;

            // Wider than the target's shape: keep the full height and cut the sides. Otherwise
            // keep the full width and cut top and bottom.
            var wider = width * target.Height > target.Width * height;
            var cropWidth = wider ? Math.Max(1, (int)Math.Round((double)height * target.Width / target.Height)) : (int)width;
            var cropHeight = wider ? (int)height : Math.Max(1, (int)Math.Round((double)width * target.Height / target.Width));

            var crop = new Rectangle((image.Width - cropWidth) / 2, (image.Height - cropHeight) / 2, cropWidth, cropHeight);

            image.Mutate(context => context
                .Crop(crop)
                .Resize(new ResizeOptions { Mode = ResizeMode.Stretch, Size = new Size(target.Width, target.Height) }));
        }

        /// <summary>
        /// The upload, resized to exactly <paramref name="target"/> the way <paramref name="fit"/>
        /// says, as a PNG with no metadata. Throws <see cref="BackgroundUploadException"/> for a
        /// file that cannot be decoded or declares more than <see cref="MaxSourcePixels"/>.
        /// </summary>
        public static byte[] Resize(byte[] file, (int Width, int Height) target, PolycomImageFit fit)
        {
            try
            {
                var info = Image.Identify(Options, file);

                if ((long)info.Width * info.Height > MaxSourcePixels)
                    throw new BackgroundUploadException($"That image is {info.Width}x{info.Height}, more than {MaxSourcePixels / 1_000_000} megapixels. Scale it down before uploading it.");

                using var image = Image.Load<Rgba32>(Options, file);

                // A phone photo's rotation is in its Exif data; apply it before measuring, or the
                // crop is taken across the wrong axis.
                image.Mutate(context => context.AutoOrient());

                if (fit == PolycomImageFit.Cover)
                    Cover(image, target);
                else
                    Contain(image, target);

                using var output = new MemoryStream();
                image.SaveAsPng(output, new PngEncoder { ColorType = PngColorType.RgbWithAlpha, SkipMetadata = true });

                return output.ToArray();
            }
            // Anything that escapes the decoder is the upload's fault, not a bug to surface raw:
            // ImageSharp throws NullReferenceException on truncated pixel data, well outside its
            // documented exception types. Only our own megapixel rejection passes through.
            catch (Exception ex) when (ex is not BackgroundUploadException)
            {
                throw new BackgroundUploadException("That image could not be read, so it could not be resized. It may be damaged, or be a kind of PNG or JPEG that is not supported.");
            }
        }
    }
}
