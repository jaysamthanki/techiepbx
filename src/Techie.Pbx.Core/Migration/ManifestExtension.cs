namespace Techie.Pbx.Core.Migration
{
    /// <summary>One <c>extensions[]</c> element of manifest v1 (docs/freepbx-import.md).</summary>
    public class ManifestExtension
    {
        public string? Name { get; set; }
        public string? Number { get; set; }
        public string? OutboundCallerID { get; set; }
        public string? Secret { get; set; }

        /// <summary><c>sip</c> or <c>pjsip</c>. Informational: both land as PJSIP (D170).</summary>
        public string? Tech { get; set; }

        public bool VoicemailAttach { get; set; } = true;
        public string? VoicemailContext { get; set; }
        public string? VoicemailEmail { get; set; }
        public bool VoicemailEnabled { get; set; }

        /// <summary>The exporter's count of messages in the tarball. The importer counts the files itself.</summary>
        public int VoicemailMessages { get; set; }

        public string? VoicemailPin { get; set; }
    }
}
