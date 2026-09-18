namespace Techie.Pbx.Web.Pages.Phones
{
    /// <summary>One line of the phones table. Most of it was written by the phone itself.</summary>
    public class PhoneRow
    {
        public bool Enabled { get; set; }

        /// <summary>The extension it registers as, as "1001 Front Desk". Empty means unassigned.</summary>
        public string Extension { get; set; } = "";

        public string Firmware { get; set; } = "";

        /// <summary>When it last fetched its config. Empty means it never has.</summary>
        public string LastConfig { get; set; } = "";

        public string LastIP { get; set; } = "";
        public string Mac { get; set; } = "";
        public string Model { get; set; } = "";
        public string Name { get; set; } = "";
        public long PhoneID { get; set; }
    }
}
