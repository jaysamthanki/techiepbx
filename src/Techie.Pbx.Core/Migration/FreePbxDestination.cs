using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Migration
{
    /// <summary>
    /// FreePBX inbound-route destinations, translated into a <see cref="Destination"/> where there
    /// is one (docs/freepbx-import.md). FreePBX writes a destination as a dialplan location,
    /// <c>context,extension,priority</c>; two of those mean "ring this extension" and both become
    /// an Extension destination:
    ///
    /// <list type="bullet">
    /// <item><c>ext-local,&lt;ext&gt;,1</c> — the extension itself.</item>
    /// <item><c>from-did-direct,&lt;ext&gt;,1</c> — a DID straight to an extension.</item>
    /// </list>
    ///
    /// Everything else — IVRs, ring groups, queues, time conditions, the <c>vmb</c>/<c>vmu</c>
    /// voicemail forms — is not v1 and translates to nothing. The caller reports the raw string so
    /// the operator can wire it by hand; it is never guessed at, because a guessed destination is
    /// a call sent somewhere nobody chose.
    /// </summary>
    public static class FreePbxDestination
    {
        /// <summary>The FreePBX contexts that mean "ring this extension".</summary>
        private static readonly HashSet<string> ExtensionContexts = new(StringComparer.Ordinal) { "ext-local", "from-did-direct" };

        /// <summary>The destination, or null when this is not one TNPBX can carry over.</summary>
        public static Destination? Translate(string? raw)
        {
            var parts = (raw ?? "").Split(',').Select(p => p.Trim()).ToArray();

            if (parts.Length != 3 || !ExtensionContexts.Contains(parts[0]) || parts[2] != "1")
                return null;

            return Extension.IsValidNumber(parts[1]) ? new Destination(DestinationType.Extension, parts[1]) : null;
        }
    }
}
