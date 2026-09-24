using Techie.Pbx.Asterisk.Provisioning;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The one site-wide Polycom logo in the data folder (D153): kept exactly as the background
    /// is, under its own fixed name, and exactly one of Poly's two logo sizes.
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

        /// <summary>A JPEG of the E100–E400 logo size, unless a test says otherwise.</summary>
        private static MemoryStream Jpeg(int width = 60, int height = 26) => new(FakeImages.Jpeg(width, height));

        /// <summary>A PNG of the E100–E400 logo size, unless a test says otherwise.</summary>
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

            var jpeg = this.store.Save(Jpeg(182, 78));

            Assert.Equal(Path.Combine(this.directory, "polycom-logo.jpg"), jpeg.Path);
            Assert.Equal(BackgroundImageFormat.Jpeg, this.store.Current()!.Format);
            Assert.Single(Directory.GetFiles(this.directory));
        }

        /// <summary>Both of Poly's logo sizes are accepted (D153).</summary>
        [Theory]
        [InlineData(60, 26)]
        [InlineData(182, 78)]
        public void Either_logo_size_is_accepted(int width, int height)
        {
            Assert.Equal(BackgroundImageFormat.Png, this.store.Save(Png(width, height)).Format);
        }

        /// <summary>
        /// Anything else is refused with the accepted sizes named, the logo already there untouched.
        /// A background-sized image is no exception.
        /// </summary>
        [Theory]
        [InlineData(320, 240)]
        [InlineData(800, 480)]
        [InlineData(61, 26)]
        [InlineData(26, 60)]
        public void Any_other_size_is_refused_and_changes_nothing(int width, int height)
        {
            this.store.Save(Png());

            var refused = Assert.Throws<BackgroundUploadException>(() => this.store.Save(Jpeg(width, height)));

            Assert.Equal($"The logo image must be exactly 60x26 or 182x78 pixels; this file is {width}x{height}.", refused.Message);
            Assert.Equal(BackgroundImageFormat.Png, this.store.Current()!.Format);
            Assert.Single(Directory.GetFiles(this.directory));
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
