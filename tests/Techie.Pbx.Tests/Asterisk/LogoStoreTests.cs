using Techie.Pbx.Asterisk.Provisioning;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The one site-wide Polycom logo in the data folder (D153, D154): kept exactly as the
    /// background is, under its own fixed name, and always 60x26 — as uploaded when it was that
    /// size, resized to a transparent-padded PNG when it was not.
    /// </summary>
    public class LogoStoreTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-logo-store-").FullName;
        private readonly LogoStore store;

        public LogoStoreTests()
        {
            this.store = new LogoStore(this.directory);
        }

        public void Dispose() => Directory.Delete(this.directory, recursive: true);

        /// <summary>A header-only JPEG of the 60x26 logo size, unless a test says otherwise.</summary>
        private static MemoryStream Jpeg(int width = 60, int height = 26) => new(FakeImages.Jpeg(width, height));

        /// <summary>A header-only PNG of the 60x26 logo size, unless a test says otherwise.</summary>
        private static MemoryStream Png(int width = 60, int height = 26) => new(FakeImages.Png(width, height));

        [Fact]
        public void A_new_install_has_no_logo()
        {
            Assert.Null(this.store.Current());
            Assert.False(this.store.Delete());
        }

        [Fact]
        public void A_logo_is_stored_under_its_own_fixed_name_in_the_data_folder()
        {
            var png = this.store.Save(Png());

            Assert.Equal(Path.Combine(this.directory, "polycom-logo.png"), png.Path);
            Assert.Equal(BackgroundImageFormat.Png, this.store.Current()!.Format);

            var jpeg = this.store.Save(Jpeg());

            Assert.Equal(Path.Combine(this.directory, "polycom-logo.jpg"), jpeg.Path);
            Assert.Equal(BackgroundImageFormat.Jpeg, this.store.Current()!.Format);
            Assert.Single(Directory.GetFiles(this.directory));
        }

        /// <summary>Already 60x26: stored byte for byte, in its own format (D154).</summary>
        [Fact]
        public void A_logo_already_60x26_is_stored_byte_for_byte()
        {
            var jpeg = EncodedImages.Jpeg(60, 26);
            var image = this.store.Save(new MemoryStream(jpeg));

            Assert.Equal(BackgroundImageFormat.Jpeg, image.Format);
            Assert.Equal(jpeg, File.ReadAllBytes(image.Path));
        }

        /// <summary>
        /// Any other size, E500's 182x78 and a background-sized image included, is resized to a
        /// 60x26 PNG (D154), replacing the JPEG that was there.
        /// </summary>
        [Theory]
        [InlineData(182, 78)]
        [InlineData(320, 240)]
        [InlineData(61, 26)]
        [InlineData(26, 60)]
        public void Any_other_size_is_resized_to_a_60x26_png(int width, int height)
        {
            this.store.Save(Jpeg());

            var image = this.store.Save(new MemoryStream(EncodedImages.Jpeg(width, height)));

            Assert.Equal(Path.Combine(this.directory, "polycom-logo.png"), image.Path);
            Assert.Equal(BackgroundImageFormat.Png, this.store.Current()!.Format);
            Assert.Single(Directory.GetFiles(this.directory));

            using var stored = EncodedImages.Decode(File.ReadAllBytes(image.Path));
            Assert.Equal((60, 26), (stored.Width, stored.Height));
        }

        /// <summary>The magic-byte check and the cap are the background's, unchanged (D152).</summary>
        [Fact]
        public void A_non_image_or_an_oversized_upload_is_refused()
        {
            using var text = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("Not an image, whatever it is called."));
            Assert.Throws<BackgroundUploadException>(() => this.store.Save(text));

            using var over = new MemoryStream(FakeImages.Png(60, 26, (int)LogoStore.MaxUploadBytes + 1));
            Assert.Throws<BackgroundUploadException>(() => this.store.Save(over));

            Assert.Null(this.store.Current());
            Assert.Empty(Directory.GetFiles(this.directory));
        }

        /// <summary>
        /// The logo and the background share the data folder and nothing else: saving or removing
        /// one never touches the other.
        /// </summary>
        [Fact]
        public void The_logo_and_the_background_are_independent()
        {
            var background = new BackgroundStore(this.directory);

            background.Save(new MemoryStream(FakeImages.Png(320, 240)));
            this.store.Save(Jpeg());

            Assert.Equal(BackgroundImageFormat.Png, background.Current()!.Format);
            Assert.Equal(BackgroundImageFormat.Jpeg, this.store.Current()!.Format);

            Assert.True(this.store.Delete());

            Assert.Null(this.store.Current());
            Assert.NotNull(background.Current());
            Assert.Equal(Path.Combine(this.directory, "polycom-background.png"), Assert.Single(Directory.GetFiles(this.directory)));
        }
    }
}
