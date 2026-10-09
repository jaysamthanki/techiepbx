using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Migration
{
    /// <summary>One outbound route the import will create, and the trunk it goes out over by planned name.</summary>
    public class PlannedOutboundRoute
    {
        public OutboundRoute Route { get; set; } = new();

        /// <summary>The FreePBX pattern it came from, for the preview.</summary>
        public string SourcePattern { get; set; } = "";

        /// <summary>The TNPBX name of the trunk, as planned; the importer resolves it to an ID.</summary>
        public string TrunkName { get; set; } = "";
    }
}
