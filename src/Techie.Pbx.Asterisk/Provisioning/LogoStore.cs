using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// The one site-wide Polycom logo (D153, D154), stored as <c>polycom-logo.png</c> or <c>.jpg</c> in
    /// the data folder beside the background, kept exactly the way the background is (see
    /// <see cref="PolycomImageStore"/>). No logo means no <c>bg.logo</c> line, and the phone keeps
    /// Poly's own.
    ///
    /// 60x26 (D154): Poly's optimal logo size for the Edge E100–E400 screens, for the same reason
    /// the background is served at the E400 size. An upload of any other shape is scaled to fit
    /// inside 60x26 and centred on transparency, so a wordmark is never cropped or distorted.
    /// </summary>
    public class LogoStore : PolycomImageStore
    {
        public LogoStore(Database database)
            : this(Path.GetDirectoryName(Path.GetFullPath(database.FilePath))!)
        {
        }

        public LogoStore(string dataDirectory)
            : base(dataDirectory, "polycom-logo", "logo", (60, 26), PolycomImageFit.Contain)
        {
        }
    }
}
