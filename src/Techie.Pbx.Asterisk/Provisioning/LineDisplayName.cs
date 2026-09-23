using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// How a provisioned phone names the line it is: the extension number, a dash, and the
    /// extension's name — "100 - Jaysam" (D146). Both failure modes the desk test found are
    /// unreadable in their own way: a bare number says who is calling but not who you are, and a
    /// bare name says neither for anyone who does not know the person. One shape for both brands,
    /// so the same site reads the same on a Poly Edge and a VVX.
    ///
    /// An extension with no name shows the number alone — a dangling dash is worse than the plain
    /// number. Pure, so both renderers call the same thing and cannot drift.
    /// </summary>
    public static class LineDisplayName
    {
        public static string For(Extension extension) =>
            string.IsNullOrWhiteSpace(extension.Name) ? extension.Number : $"{extension.Number} - {extension.Name}";
    }
}
