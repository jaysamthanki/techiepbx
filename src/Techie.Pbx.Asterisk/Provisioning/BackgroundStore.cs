using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// The one site-wide Polycom background image (D145, D151), stored as
    /// <c>polycom-background.png</c> or <c>.jpg</c> in the data folder. Everything about how it is
    /// kept is <see cref="PolycomImageStore"/>'s; what is particular to the background is the name
    /// and the two sizes it must be.
    ///
    /// Exactly 320x240 or 800x480 (D153): Poly's optimal background sizes for the Edge E100–E400
    /// screens and the E500 series. The site's fleet is mixed-model but mostly E450, and one image
    /// serves all of it, so the check is strict on purpose — an image of any other size is one the
    /// phone scales, and the admin should hear that at upload rather than see it on a desk.
    /// </summary>
    public class BackgroundStore : PolycomImageStore
    {
        public BackgroundStore(Database database)
            : this(Path.GetDirectoryName(Path.GetFullPath(database.FilePath))!)
        {
        }

        public BackgroundStore(string dataDirectory)
            : base(dataDirectory, "polycom-background", "background", (320, 240), (800, 480))
        {
        }
    }
}
