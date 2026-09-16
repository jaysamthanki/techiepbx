using Techie.Pbx.Asterisk.Ami;

namespace Techie.Pbx.Web.Pages.Trunks
{
    /// <summary>
    /// One line of the trunks table. Deliberately not the <c>Trunk</c> model: the table must never
    /// carry provider passwords to the browser.
    /// </summary>
    public class TrunkRow
    {
        public bool Enabled { get; set; }
        public string Name { get; set; } = "";
        public bool Register { get; set; }

        /// <summary>Host and port as one column, the way an admin reads it.</summary>
        public string Server { get; set; } = "";

        public RegistrationState State { get; set; }
        public long TrunkID { get; set; }
    }
}
