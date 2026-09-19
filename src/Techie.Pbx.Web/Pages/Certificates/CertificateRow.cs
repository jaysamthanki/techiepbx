namespace Techie.Pbx.Web.Pages.Certificates
{
    /// <summary>
    /// One row of the certificates table: what the database holds, already turned into the words
    /// and the colour the page shows, so the view has no logic in it.
    /// </summary>
    public class CertificateRow
    {
        public long CertificateID { get; set; }

        /// <summary>The bootstrap contextual class for the status badge.</summary>
        public string BadgeClass { get; set; } = "bg-secondary";

        /// <summary>How many days are left, or "—" when nothing has been issued.</summary>
        public string DaysLeft { get; set; } = "—";

        /// <summary>The expiry as a date, or "—".</summary>
        public string Expires { get; set; } = "—";

        public string Hostnames { get; set; } = "";

        /// <summary>The last failed order's message, or empty. Shown under the status.</summary>
        public string LastError { get; set; } = "";

        public string Name { get; set; } = "";

        /// <summary>
        /// Set when the row is usable but the running web server is serving something else, which
        /// happens between a renewal and the next restart (D99).
        /// </summary>
        public string Note { get; set; } = "";

        /// <summary>Active, Expired, Not issued, Disabled or Failed.</summary>
        public string Status { get; set; } = "";
    }
}
