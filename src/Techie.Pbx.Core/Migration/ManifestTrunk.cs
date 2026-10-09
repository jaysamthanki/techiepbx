namespace Techie.Pbx.Core.Migration
{
    /// <summary>One <c>trunks[]</c> element of manifest v1 (docs/freepbx-import.md).</summary>
    public class ManifestTrunk
    {
        public string? AuthUsername { get; set; }
        public string? CallerIDNumber { get; set; }
        public string? Codecs { get; set; }

        /// <summary>Whether FreePBX had the trunk switched off. Informational: every trunk lands disabled (D170).</summary>
        public bool DisabledInFreePBX { get; set; }

        public string? FromDomain { get; set; }
        public string? FromUser { get; set; }
        public string? MatchAddresses { get; set; }
        public string? Name { get; set; }
        public string? Password { get; set; }
        public bool Register { get; set; }
        public string? ServerHost { get; set; }
        public int ServerPort { get; set; } = 5060;
        public string? Tech { get; set; }
        public string? Username { get; set; }
    }
}
