using Techie.Pbx.Core.Migration;

namespace Techie.Pbx.Asterisk.Migration
{
    /// <summary>
    /// What an import actually wrote, and every warning — the plan's and the ones that only
    /// showed up while writing (D170). The report page is this.
    /// </summary>
    public class ImportReport
    {
        public List<string> Announcements { get; set; } = new();

        /// <summary>Whether the apply after the import failed; <see cref="ApplySummary"/> says how.</summary>
        public bool ApplyFailed { get; set; }

        /// <summary>What the apply after the import did, in the words the navbar's apply uses.</summary>
        public string ApplySummary { get; set; } = "";

        public List<string> Extensions { get; set; } = new();

        /// <summary>"DID 17142029302 on callcentric", one per row written.</summary>
        public List<string> InboundRoutes { get; set; } = new();

        public List<string> OutboundRoutes { get; set; } = new();
        public List<string> Phones { get; set; } = new();

        /// <summary>
        /// Whether the staging directory was kept because something in it still has to be copied
        /// by hand — voicemail the web user could not write into the spool.
        /// </summary>
        public bool StagingKept { get; set; }

        /// <summary>Every one of these is disabled (D170).</summary>
        public List<string> Trunks { get; set; } = new();

        public int VoicemailMessages { get; set; }
        public List<MigrationWarning> Warnings { get; set; } = new();
    }
}
