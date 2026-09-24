using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// The one site-wide Polycom logo (D153), stored as <c>polycom-logo.png</c> or <c>.jpg</c> in
    /// the data folder beside the background, kept exactly the way the background is (see
    /// <see cref="PolycomImageStore"/>). No logo means no <c>bg.logo</c> line, and the phone keeps
    /// Poly's own.
    ///
    /// Exactly 60x26 or 182x78 (D153): Poly's optimal logo sizes for the Edge E100–E400 screens and
    /// the E500 series. Strict for the same reason as the background — one image for a mixed,
    /// mostly-E450 fleet, so a size the phone would have to scale is refused where it can be
    /// explained.
    /// </summary>
    public class LogoStore : PolycomImageStore
    {
        public LogoStore(Database database)
            : this(Path.GetDirectoryName(Path.GetFullPath(database.FilePath))!)
        {
        }

        public LogoStore(string dataDirectory)
            : base(dataDirectory, "polycom-logo", "logo", (60, 26), (182, 78))
        {
        }
    }
}
