using System.Text;
using Techie.Pbx.Asterisk.Ami;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders manager.conf from the same <see cref="AmiSettings"/> the app connects with, so the
    /// credentials in the file and the credentials in the database cannot drift apart.
    ///
    /// AMI listens on the loopback address and nothing else, whatever the settings say (D32), and
    /// the one account it defines gets only the permissions our four actions need.
    /// </summary>
    public static class ManagerConfRenderer
    {
        /// <summary>The only address AMI is ever bound to.</summary>
        public const string BindAddress = "127.0.0.1";

        /// <summary>
        /// Read: "system" carries the answers to PJSIPShowContacts. Write: "system" and "config"
        /// are between them what Action: Reload needs. No "command" permission, because we do not
        /// use Action: Command (D17), and no "originate", because nothing here places calls.
        /// </summary>
        private const string ReadPermissions = "system";

        private const string WritePermissions = "system,config";

        public static string Render(AmiSettings ami)
        {
            var errors = ami.Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException("Invalid AMI settings: " + string.Join(" ", errors));

            // The app is the only client, and it has to be able to reach what we bind. Anything
            // but loopback means the settings and this file disagree, and the disagreement would
            // only show up as a broken apply on a live system.
            if (!IsLoopback(ami.Host))
            {
                throw new InvalidOperationException(
                    $"AMI host is '{ami.Host}', but AMI is bound to {BindAddress} and nothing else. " +
                    "Set Ami.Host to 127.0.0.1, or move the setting, not the binding.");
            }

            var username = ConfText.Safe(ami.Username, "AMI username");
            var secret = ConfText.Safe(ami.Secret, "AMI secret");

            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[general]\n");
            sb.Append("enabled = yes\n");
            sb.Append($"port = {ami.Port}\n");
            sb.Append($"bindaddr = {BindAddress}\n");

            // AMI over HTTP would put this on a network socket by another route.
            sb.Append("webenabled = no\n");

            sb.Append('\n');
            sb.Append($"[{username}]\n");
            sb.Append($"secret = {secret}\n");
            sb.Append("deny = 0.0.0.0/0.0.0.0\n");
            sb.Append($"permit = {BindAddress}/255.255.255.255\n");
            sb.Append($"read = {ReadPermissions}\n");
            sb.Append($"write = {WritePermissions}\n");

            return sb.ToString();
        }

        private static bool IsLoopback(string host) =>
            string.Equals(host, BindAddress, StringComparison.Ordinal) ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
    }
}
