namespace Techie.Pbx.Web.Pages.Connectivity
{
    /// <summary>
    /// One line of the cheat sheet's extension list (D120). Deliberately not the
    /// <c>Extension</c> model, for the reason the extensions table is not either: this page is
    /// printed and pinned to a wall, and the model carries SIP passwords and voicemail PINs.
    /// </summary>
    public class CheatSheetExtension
    {
        public string Name { get; set; } = "";
        public string Number { get; set; } = "";
    }
}
