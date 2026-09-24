using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// The one site-wide Polycom background image (D145, D151), stored as
    /// <c>polycom-background.png</c> or <c>.jpg</c> in the data folder. Everything about how it is
    /// kept is <see cref="PolycomImageStore"/>'s; what is particular to the background is the name,
    /// the size and how an upload is fitted to it.
    ///
    /// 320x240 (D154): Poly's optimal background size for the Edge E100–E400 screens. The site has
    /// one background for a mixed-model, mostly-E450 fleet, so it is served at the E400 size
    /// whatever was uploaded. An upload of any other shape is scaled to cover 320x240 and
    /// centre-cropped, as wallpaper is: the screen is always filled and the edges are what is lost.
    /// </summary>
    public class BackgroundStore : PolycomImageStore
    {
        public BackgroundStore(Database database)
            : this(Path.GetDirectoryName(Path.GetFullPath(database.FilePath))!)
        {
        }

        public BackgroundStore(string dataDirectory)
            : base(dataDirectory, "polycom-background", "background", (320, 240), PolycomImageFit.Cover)
        {
        }
    }
}
