using System.Text;
using Techie.Pbx.Asterisk.Provisioning;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The one site-wide Polycom background image in the data folder (D151, D152, D153, D154): at
    /// most one file, under a fixed name, always 320x240 — kept byte for byte when it was uploaded
    /// that size, resized to a PNG when it was not — and nothing refused ever touches the image
    /// that is there.
    /// </summary>
    public class BackgroundStoreTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-background-store-").FullName;
        private readonly BackgroundStore store;

        public BackgroundStoreTests()
        {
            this.store = new BackgroundStore(this.directory);
        }

        public void Dispose() => Directory.Delete(this.directory, recursive: true);

        /// <summary>A header-only JPEG of the 320x240 background size, unless a test says otherwise.</summary>
        private static MemoryStream Jpeg(int bytes = 4096, int width = 320, int height = 240) =>
            new(FakeImages.Jpeg(width, height, bytes));

        /// <summary>A header-only PNG of the 320x240 background size, unless a test says otherwise.</summary>
        private static MemoryStream Png(int bytes = 4096, int width = 320, int height = 240) =>
            new(FakeImages.Png(width, height, bytes));

        [Fact]
        public void A_new_install_has_no_image()
        {
            Assert.Null(this.store.Current());
            Assert.False(this.store.Delete());
        }

        [Fact]
        public void A_png_is_stored_under_its_fixed_name_in_the_data_folder()
        {
            var image = this.store.Save(Png());

            Assert.Equal(BackgroundImageFormat.Png, image.Format);
            Assert.Equal(Path.Combine(this.directory, "polycom-background.png"), image.Path);
            Assert.Equal(4096, image.Bytes);
            Assert.True(File.Exists(image.Path));
            Assert.Equal(BackgroundImageFormat.Png, this.store.Current()!.Format);
        }

        /// <summary>One image at a time: a JPEG replacing a PNG leaves no PNG behind to be found first.</summary>
        [Fact]
        public void A_new_image_replaces_the_old_one_whatever_its_format()
        {
            this.store.Save(Png());
            var image = this.store.Save(Jpeg());

            Assert.Equal(BackgroundImageFormat.Jpeg, this.store.Current()!.Format);
            Assert.Equal(Path.Combine(this.directory, "polycom-background.jpg"), image.Path);
            Assert.False(File.Exists(this.store.PathFor(BackgroundImageFormat.Png)));
            Assert.Single(Directory.GetFiles(this.directory));
        }

        /// <summary>A text file called logo.png is text: refused, and the image already there is untouched.</summary>
        [Fact]
        public void A_text_file_renamed_png_is_refused_and_changes_nothing()
        {
            this.store.Save(Png());

            using var text = new MemoryStream(Encoding.ASCII.GetBytes("Not an image at all, despite the .png on the end."));

            Assert.Throws<BackgroundUploadException>(() => this.store.Save(text));
            Assert.Equal(BackgroundImageFormat.Png, this.store.Current()!.Format);
            Assert.Single(Directory.GetFiles(this.directory));
        }

        /// <summary>Exactly the cap is fine; one byte more is refused and leaves no spool file behind.</summary>
        [Fact]
        public void An_image_over_the_size_cap_is_refused()
        {
            using var over = Png((int)BackgroundStore.MaxUploadBytes + 1);

            Assert.Throws<BackgroundUploadException>(() => this.store.Save(over));
            Assert.Null(this.store.Current());
            Assert.Empty(Directory.GetFiles(this.directory));

            using var atCap = Png((int)BackgroundStore.MaxUploadBytes);
            Assert.Equal(BackgroundStore.MaxUploadBytes, this.store.Save(atCap).Bytes);
        }

        [Fact]
        public void An_empty_upload_is_refused()
        {
            using var empty = new MemoryStream();

            Assert.Throws<BackgroundUploadException>(() => this.store.Save(empty));
            Assert.Empty(Directory.GetFiles(this.directory));
        }

        [Fact]
        public void Removing_the_image_leaves_no_image()
        {
            this.store.Save(Jpeg());

            Assert.True(this.store.Delete());
            Assert.Null(this.store.Current());
            Assert.Empty(Directory.GetFiles(this.directory));
        }

        /// <summary>
        /// Already 320x240: stored exactly as uploaded, in its own format, never re-encoded (D154).
        /// Header-only fakes prove nothing decoded it; a real JPEG proves it stays a JPEG.
        /// </summary>
        [Fact]
        public void An_upload_already_320x240_is_stored_byte_for_byte()
        {
            var fake = FakeImages.Png(320, 240);
            Assert.Equal(fake, File.ReadAllBytes(this.store.Save(new MemoryStream(fake)).Path));

            var jpeg = EncodedImages.Jpeg(320, 240);
            var image = this.store.Save(new MemoryStream(jpeg));

            Assert.Equal(BackgroundImageFormat.Jpeg, image.Format);
            Assert.Equal(Path.Combine(this.directory, "polycom-background.jpg"), image.Path);
            Assert.Equal(jpeg, File.ReadAllBytes(image.Path));
        }

        /// <summary>
        /// Any other size, E500's 800x480 included, is resized to 320x240 and stored as a PNG
        /// whatever it was uploaded as (D154) — replacing a JPEG that was there.
        /// </summary>
        [Theory]
        [InlineData(800, 480)]
        [InlineData(1024, 600)]
        [InlineData(321, 240)]
        [InlineData(240, 320)]
        [InlineData(60, 26)]
        public void Any_other_size_is_resized_to_a_320x240_png(int width, int height)
        {
            this.store.Save(new MemoryStream(EncodedImages.Jpeg(320, 240)));

            var image = this.store.Save(new MemoryStream(EncodedImages.Jpeg(width, height)));

            Assert.Equal(BackgroundImageFormat.Png, image.Format);
            Assert.Equal(Path.Combine(this.directory, "polycom-background.png"), image.Path);
            Assert.Equal(BackgroundImageFormat.Png, this.store.Current()!.Format);
            Assert.Single(Directory.GetFiles(this.directory));

            using var stored = EncodedImages.Decode(File.ReadAllBytes(image.Path));
            Assert.Equal((320, 240), (stored.Width, stored.Height));
        }

        /// <summary>
        /// A header that claims another size but has no image behind it has to be decoded to be
        /// resized, and cannot be: refused, and the image already there is untouched.
        /// </summary>
        [Fact]
        public void An_upload_that_needs_resizing_but_cannot_be_decoded_is_refused_and_changes_nothing()
        {
            this.store.Save(Png());

            var refused = Assert.Throws<BackgroundUploadException>(() => this.store.Save(Jpeg(width: 800, height: 480)));

            Assert.Contains("could not be read", refused.Message);
            Assert.Equal(BackgroundImageFormat.Png, this.store.Current()!.Format);
            Assert.Single(Directory.GetFiles(this.directory));
        }

        /// <summary>A JPEG signature with no readable frame header has no size and cannot be decoded, so it is refused.</summary>
        [Fact]
        public void A_jpeg_whose_size_cannot_be_read_is_refused()
        {
            var content = new byte[4096];
            new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.CopyTo(content, 0);

            using var unreadable = new MemoryStream(content);

            var refused = Assert.Throws<BackgroundUploadException>(() => this.store.Save(unreadable));

            Assert.Contains("could not be read", refused.Message);
            Assert.Null(this.store.Current());
            Assert.Empty(Directory.GetFiles(this.directory));
        }
    }
}
