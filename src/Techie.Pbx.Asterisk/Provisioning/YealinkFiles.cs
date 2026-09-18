using System.Text.RegularExpressions;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// The two file names a Yealink phone asks the provisioning endpoint for, and the only two it
    /// gets an answer to (D88, mirroring D77):
    ///
    /// <code>
    /// GET /yealink/y000000000000.boot   the fixed boot file every Yealink phone asks for first
    /// GET /yealink/0004f2aabbcc.cfg     the phone's own configuration, named after its bare MAC
    /// </code>
    ///
    /// Unlike Polycom's master file, the boot file name carries no MAC at all — it is the same
    /// fixed name for every phone (D89) — and the per-device file is named after the bare MAC
    /// rather than an "exten"-prefixed one. Both are matched with anchored patterns rather than
    /// picked apart by hand, because the MAC in the config file name is the key a database row is
    /// looked up by. Nothing that is not twelve lowercase hex digits gets that far.
    /// </summary>
    public static partial class YealinkFiles
    {
        /// <summary>The fixed name every Yealink phone asks for first, with no MAC in it (D89).</summary>
        public const string BootFileName = "y000000000000.boot";

        /// <summary>The name of the per-phone config file: the bare MAC address, plus ".cfg".</summary>
        public static string ConfigFileName(string mac) => $"{mac}.cfg";

        public static bool IsBootFile(string? fileName) => string.Equals(fileName, BootFileName, StringComparison.Ordinal);

        /// <summary>Whether this is a request for a phone's configuration, and for which MAC.</summary>
        public static bool TryParseConfig(string? fileName, out string mac)
        {
            mac = "";

            if (string.IsNullOrEmpty(fileName))
                return false;

            var match = ConfigPattern().Match(fileName);
            if (!match.Success)
                return false;

            mac = match.Groups["mac"].Value;
            return true;
        }

        [GeneratedRegex(@"^(?<mac>[0-9a-f]{12})\.cfg$")]
        private static partial Regex ConfigPattern();
    }
}
