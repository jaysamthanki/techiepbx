using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Migration
{
    /// <summary>
    /// Exactly what an import will write, worked out before anything is written: the preview shows
    /// this, and the import writes this and nothing else. Built by <see cref="ImportPlanner"/>.
    /// </summary>
    public class ImportPlan
    {
        /// <summary>How many FreePBX chan_sip extensions become PJSIP endpoints (D170).</summary>
        public int ChanSipExtensions { get; set; }

        public List<Extension> Extensions { get; set; } = new();

        /// <summary>The FreePBX inbound routes that will land, before each is multiplied by the trunks.</summary>
        public int InboundRouteCount { get; set; }

        public List<PlannedInboundRoute> InboundRoutes { get; set; } = new();
        public List<PlannedMailbox> Mailboxes { get; set; } = new();
        public List<PlannedOutboundRoute> OutboundRoutes { get; set; } = new();
        public List<PlannedPhone> Phones { get; set; } = new();
        public List<PlannedSound> Sounds { get; set; } = new();

        /// <summary>What the exporter ran on, for the preview's heading.</summary>
        public string Source { get; set; } = "";

        /// <summary>Every trunk lands with <c>Enabled = false</c> (D170).</summary>
        public List<Trunk> Trunks { get; set; } = new();

        public int VoicemailMessages => this.Mailboxes.Sum(m => m.Messages);
        public List<MigrationWarning> Warnings { get; set; } = new();
    }
}
