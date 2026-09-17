namespace Techie.Pbx.Web.Pages.Ivrs
{
    /// <summary>One line of the IVRs table.</summary>
    public class IvrRow
    {
        /// <summary>Whether a caller may dial an extension straight from the menu (D60).</summary>
        public bool DirectDial { get; set; }

        public bool Enabled { get; set; }

        /// <summary>The announcement this menu greets with, by name.</summary>
        public string Greeting { get; set; } = "";

        /// <summary>
        /// Whether that greeting is one the menu could actually play: still there, switched on and
        /// with audio. When it is not, the menu is left out of the dialplan entirely (D58), which
        /// is worth showing differently from a normal row.
        /// </summary>
        public bool GreetingUsable { get; set; }

        public long IvrID { get; set; }
        public string Name { get; set; } = "";

        /// <summary>The number to dial to hear the menu, or an em dash when there is none.</summary>
        public string PlayExtension { get; set; } = "";

        public int Retries { get; set; }
        public int TimeoutSeconds { get; set; }
    }
}
