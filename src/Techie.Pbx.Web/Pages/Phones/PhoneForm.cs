using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Phones
{
    /// <summary>
    /// What the edit form in the modal shows, and what it posts back. Only three of these fields
    /// are posted — the name, the extension and the enabled flag — because everything else about a
    /// phone was written by the phone, and an admin editing it would only be editing a record of
    /// what happened (D78). The rest is shown, read-only, so the person assigning an extension can
    /// see which handset they are looking at.
    /// </summary>
    public class PhoneForm
    {
        public bool Enabled { get; set; } = true;
        public List<string> Errors { get; set; } = new();

        /// <summary>The extension to register as. Null or 0 is "unassigned".</summary>
        public long? ExtensionID { get; set; }

        /// <summary>Every extension this phone could be given. Not posted back.</summary>
        public List<Extension> Extensions { get; set; } = new();

        public string Firmware { get; set; } = "";
        public string LastConfig { get; set; } = "";
        public string LastIP { get; set; } = "";

        /// <summary>
        /// The port this phone is told to speak SIP from, shown because it is the first thing to
        /// check when two phones behind one NAT misbehave (D81).
        /// </summary>
        public int LocalSipPort { get; set; }

        public string Mac { get; set; } = "";
        public string Model { get; set; } = "";
        public string? Name { get; set; } = "";
        public long PhoneID { get; set; }
    }
}
