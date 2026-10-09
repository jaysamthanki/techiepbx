namespace Techie.Pbx.Core.Migration
{
    /// <summary>One <c>inboundRoutes[]</c> element of manifest v1 (docs/freepbx-import.md).</summary>
    public class ManifestInboundRoute
    {
        public string? CallerIDMatch { get; set; }
        public bool CatchAll { get; set; }
        public string? Description { get; set; }

        /// <summary>The raw FreePBX destination, e.g. <c>ext-local,101,1</c>.</summary>
        public string? Destination { get; set; }

        public string? DID { get; set; }
        public string? MohClass { get; set; }
        public string? TrunkName { get; set; }
    }
}
