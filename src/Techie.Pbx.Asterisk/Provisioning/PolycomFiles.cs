using System.Text.RegularExpressions;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// The two file names a Polycom phone asks the provisioning endpoint for, and the only two it
    /// gets an answer to (D77):
    ///
    /// <code>
    /// GET /polycom/0004f2aabbcc.cfg        the master file, which names the next one
    /// GET /polycom/exten0004f2aabbcc.cfg   the phone's actual configuration
    /// </code>
    ///
    /// Both names are matched with anchored regexes rather than picked apart by hand, because the
    /// MAC address in them is the key a database row is looked up by. Nothing that is not twelve
    /// lowercase hex digits gets that far.
    /// </summary>
    public static partial class PolycomFiles
    {
        /// <summary>The name of the per-phone config file, which the master file points at.</summary>
        public static string ConfigFileName(string mac) => $"exten{mac}.cfg";

        /// <summary>The name of the master file, which is the phone's own MAC address.</summary>
        public static string MasterFileName(string mac) => $"{mac}.cfg";

        /// <summary>Whether this is a request for a phone's configuration, and for which MAC.</summary>
        public static bool TryParseConfig(string? fileName, out string mac) =>
            TryParse(ConfigPattern(), fileName, out mac);

        /// <summary>Whether this is a request for a master file, and for which MAC.</summary>
        public static bool TryParseMaster(string? fileName, out string mac) =>
            TryParse(MasterPattern(), fileName, out mac);

        private static bool TryParse(Regex pattern, string? fileName, out string mac)
        {
            mac = "";

            if (string.IsNullOrEmpty(fileName))
                return false;

            var match = pattern.Match(fileName);
            if (!match.Success)
                return false;

            mac = match.Groups["mac"].Value;
            return true;
        }

        [GeneratedRegex(@"^exten(?<mac>[0-9a-f]{12})\.cfg\z")]
        private static partial Regex ConfigPattern();

        [GeneratedRegex(@"^(?<mac>[0-9a-f]{12})\.cfg\z")]
        private static partial Regex MasterPattern();
    }
}
