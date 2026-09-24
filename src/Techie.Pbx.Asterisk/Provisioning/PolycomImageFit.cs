namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// How an upload that is not already the target size is made into it (D154).
    /// </summary>
    public enum PolycomImageFit
    {
        /// <summary>
        /// Scaled until it covers the whole target, then centre-cropped: the background, which
        /// should fill the screen, and loses its edges as any wallpaper does.
        /// </summary>
        Cover,

        /// <summary>
        /// Scaled until it fits inside the target, then centred on transparency: the logo, which
        /// must never be cropped or stretched.
        /// </summary>
        Contain,
    }
}
