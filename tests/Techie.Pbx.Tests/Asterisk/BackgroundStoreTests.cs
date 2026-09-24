using System.Text;
using Techie.Pbx.Asterisk.Provisioning;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The one site-wide Polycom background image in the data folder (D151, D152): at most one
    /// file, under a fixed name, and nothing refused ever touches the image that is there.
    /// </summary>
    public class BackgroundStoreTests : IDisposable
    {
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-background-store-").FullName;
        private readonly BackgroundStore store;

        public BackgroundStoreTests()
        {
            this.store = new BackgroundStore(this.directory);
        }

        public void Dispose() => Directory.Delete(this.directory, recursive: true);

        private static MemoryStream Jpeg(int bytes = 4096) => Image(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, bytes);

        private static MemoryStream Image(byte[] signature, int bytes)
        {
            var content = new byte[bytes];
            signature.CopyTo(content, 0);
            return new MemoryStream(content);
        }

        private static MemoryStream Png(int bytes = 4096) => Image(PngSignature, bytes);

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
    }
}
