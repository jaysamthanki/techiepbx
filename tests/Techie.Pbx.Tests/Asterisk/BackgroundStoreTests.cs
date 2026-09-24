using System.Text;
using Techie.Pbx.Asterisk.Provisioning;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The one site-wide Polycom background image in the data folder (D151, D152, D153): at most
    /// one file, under a fixed name, exactly one of Poly's two background sizes, and nothing
    /// refused ever touches the image that is there.
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

        /// <summary>A JPEG of the E100–E400 background size, unless a test says otherwise.</summary>
        private static MemoryStream Jpeg(int bytes = 4096, int width = 320, int height = 240) =>
            new(FakeImages.Jpeg(width, height, bytes));

        /// <summary>A PNG of the E100–E400 background size, unless a test says otherwise.</summary>
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

        /// <summary>Both of Poly's background sizes are accepted, in either format (D153).</summary>
        [Theory]
        [InlineData(320, 240)]
        [InlineData(800, 480)]
        public void Either_background_size_is_accepted(int width, int height)
        {
            Assert.Equal(BackgroundImageFormat.Png, this.store.Save(Png(width: width, height: height)).Format);
            Assert.Equal(BackgroundImageFormat.Jpeg, this.store.Save(Jpeg(width: width, height: height)).Format);
        }

        /// <summary>
        /// Anything else is refused, with a message naming the sizes that are accepted and the one
        /// the file is — and the image already there is untouched. A logo-sized image is no
        /// exception, and neither is one a pixel out.
        /// </summary>
        [Theory]
        [InlineData(1024, 600)]
        [InlineData(321, 240)]
        [InlineData(240, 320)]
        [InlineData(60, 26)]
        [InlineData(182, 78)]
        public void Any_other_size_is_refused_and_changes_nothing(int width, int height)
        {
            this.store.Save(Png());

            var refused = Assert.Throws<BackgroundUploadException>(() => this.store.Save(Jpeg(width: width, height: height)));

            Assert.Equal($"The background image must be exactly 320x240 or 800x480 pixels; this file is {width}x{height}.", refused.Message);
            Assert.Equal(BackgroundImageFormat.Png, this.store.Current()!.Format);
            Assert.Single(Directory.GetFiles(this.directory));
        }

        /// <summary>A JPEG signature with no readable frame header has no size to check, so it is refused.</summary>
        [Fact]
        public void A_jpeg_whose_size_cannot_be_read_is_refused()
        {
            var content = new byte[4096];
            new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.CopyTo(content, 0);

            using var unreadable = new MemoryStream(content);

            var refused = Assert.Throws<BackgroundUploadException>(() => this.store.Save(unreadable));

            Assert.Contains("320x240 or 800x480", refused.Message);
            Assert.Null(this.store.Current());
            Assert.Empty(Directory.GetFiles(this.directory));
        }
    }
}
