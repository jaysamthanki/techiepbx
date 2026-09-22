using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Reports
{
    /// <summary>
    /// The PJSIP endpoints our generated pjsip.conf has, as the reports and the status page read
    /// them: trunks by name, and extensions by number with the name an admin gave them. An endpoint
    /// that is not a trunk is an extension, as <see cref="CdrChannels"/> has it, whether or not it
    /// still has a row: a deleted extension's old calls still say which number made them.
    /// </summary>
    public class PbxEndpoints
    {
        /// <summary>Extension numbers and their names; a number with no name is left out.</summary>
        public IReadOnlyDictionary<string, string> ExtensionNames { get; }

        public IReadOnlySet<string> TrunkNames { get; }

        public PbxEndpoints(IReadOnlyDictionary<string, string> extensionNames, IReadOnlySet<string> trunkNames)
        {
            this.ExtensionNames = extensionNames;
            this.TrunkNames = trunkNames;
        }

        /// <summary>The endpoints as the Extensions and Trunks tables have them now.</summary>
        public static PbxEndpoints From(IEnumerable<Extension> extensions, IEnumerable<Trunk> trunks) => new(
            extensions
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .ToDictionary(e => e.Number, e => e.Name.Trim(), StringComparer.Ordinal),
            trunks.Select(t => t.Name).ToHashSet(StringComparer.Ordinal));

        /// <summary>Whether an endpoint is one of our extensions: there is one, and it is not a trunk.</summary>
        public bool IsExtension(string? endpoint) => endpoint != null && !this.TrunkNames.Contains(endpoint);

        public bool IsTrunk(string? endpoint) => endpoint != null && this.TrunkNames.Contains(endpoint);

        /// <summary>An extension as a person reads it: "100 (Front Desk)", or the number alone when it has no name.</summary>
        public string Label(string number) =>
            this.ExtensionNames.TryGetValue(number, out var name) ? $"{number} ({name})" : number;
    }
}
