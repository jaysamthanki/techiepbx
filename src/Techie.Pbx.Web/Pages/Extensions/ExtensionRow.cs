using Techie.Pbx.Asterisk.Ami;

namespace Techie.Pbx.Web.Pages.Extensions
{
    /// <summary>
    /// One line of the extensions table. Deliberately not the <c>Extension</c> model: the table
    /// must never carry SIP passwords to the browser.
    /// </summary>
    public class ExtensionRow
    {
        public bool Enabled { get; set; }
        public long ExtensionID { get; set; }
        public string Name { get; set; } = "";
        public string Number { get; set; } = "";
        public RegistrationState State { get; set; }
    }
}
