namespace Techie.Pbx.Core.Migration
{
    /// <summary>
    /// One <c>outboundRoutes[]</c> element of manifest v1: one FreePBX route pattern. A FreePBX
    /// route with several patterns is several of these sharing a name and priority.
    /// </summary>
    public class ManifestOutboundRoute
    {
        public string? CallerID { get; set; }

        /// <summary>FreePBX pattern grammar, exactly as FreePBX stored it (D170).</summary>
        public string? DialPattern { get; set; }

        public bool Emergency { get; set; }
        public string? Name { get; set; }
        public string? PrependDigits { get; set; }
        public int Priority { get; set; }

        /// <summary>Optional in v1: the exporter may leave it out, which means none.</summary>
        public int StripDigits { get; set; }

        public string? TrunkName { get; set; }
        public List<string?>? TrunkSequence { get; set; }
    }
}
