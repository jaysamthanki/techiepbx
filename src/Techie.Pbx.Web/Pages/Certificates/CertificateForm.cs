namespace Techie.Pbx.Web.Pages.Certificates
{
    /// <summary>
    /// What the order/edit modal posts. A certificate has very little an admin chooses: what to
    /// call it, which hostnames it is for, and whether it is in use. Everything else about it —
    /// the PEMs, the expiry, the last error — is the ACME server's answer, not a field.
    /// </summary>
    public class CertificateForm
    {
        public long CertificateID { get; set; }

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// As typed: commas, spaces or one per line, all of which the model splits the same way.
        /// A textarea because a list of names is easier to check when it is one per line.
        /// </summary>
        public string Hostnames { get; set; } = "";

        public string Name { get; set; } = "";

        /// <summary>Problems to show above the form, from validation or from a failed order.</summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>Whether this certificate has ever been issued, which is what Renew needs.</summary>
        public bool IsIssued { get; set; }

        public bool IsNew => this.CertificateID == 0;
    }
}
