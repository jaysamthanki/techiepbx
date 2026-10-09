using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Migration
{
    /// <summary>
    /// One inbound route the import will create. FreePBX routes are not bound to a trunk, so one
    /// manifest route becomes one of these per imported trunk.
    /// </summary>
    public class PlannedInboundRoute
    {
        public InboundRoute Route { get; set; } = new();

        /// <summary>The FreePBX destination it came from, for the preview.</summary>
        public string SourceDestination { get; set; } = "";

        /// <summary>The TNPBX name of the trunk, as planned; the importer resolves it to an ID.</summary>
        public string TrunkName { get; set; } = "";
    }
}
