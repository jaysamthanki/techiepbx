using System.Diagnostics;
using log4net;
using Techie.Pbx.Contracts;

namespace Techie.Pbx.Helper.Firewall
{
    /// <summary>
    /// The firewall half of the Helper: render a ruleset, check it, load it, and remember what was
    /// loaded (D142).
    ///
    /// nft is run through <see cref="ProcessStartInfo.ArgumentList"/> at a fixed absolute path,
    /// never a command line and never a shell, and every argument is either a constant or a path
    /// this class chose — nothing a message carried is ever an argument. What a message can
    /// influence is the contents of the file, and that file is <see cref="NftRuleset"/>'s output
    /// from validated rules.
    ///
    /// A ruleset is checked with <c>nft -c -f</c> before it is loaded with <c>nft -f</c>, and a
    /// failure at either step leaves the running ruleset exactly as it was.
    /// </summary>
    public class NftFirewall
    {
        /// <summary>The ruleset last loaded, kept so a reboot re-applies it before anything else runs.</summary>
        public const string LastRulesetFile = "last.nft";

        /// <summary>
        /// The rules of that apply and when it happened, as firewall.status answers it. Beside
        /// the ruleset rather than parsed back out of it: the Helper is not in the business of
        /// reading nft syntax, and a status the admin sees has to survive a restart of this
        /// process or the firewall page would report "never applied" on an armed machine.
        /// </summary>
        public const string LastStatusFile = "last.json";

        /// <summary>Debian's path. Fixed, so nothing here ever searches PATH for a program to run as root.</summary>
        public const string NftPath = "/usr/sbin/nft";

        /// <summary>Long enough for a ruleset load, short enough that a wedged nft is not a wedged Helper.</summary>
        public const int NftTimeoutMilliseconds = 10000;

        /// <summary>0700 root:root: the Helper's own state, which no other account has any business reading.</summary>
        public const string StateDirectory = "/var/lib/tnpbx-helper";

        private static readonly ILog Log = LogManager.GetLogger(typeof(NftFirewall));

        private readonly string pendingPath;
        private readonly string rulesetPath;
        private readonly string stateDirectory;
        private readonly string statusPath;
        private FirewallStatus status = new();

        public NftFirewall() : this(StateDirectory)
        {
        }

        public NftFirewall(string stateDirectory)
        {
            this.pendingPath = Path.Combine(stateDirectory, "pending.nft");
            this.rulesetPath = Path.Combine(stateDirectory, LastRulesetFile);
            this.stateDirectory = stateDirectory;
            this.statusPath = Path.Combine(stateDirectory, LastStatusFile);
        }

        /// <summary>
        /// Replaces the ruleset with the safety rules plus these. The rules are validated again
        /// here — the Helper never trusts what arrived on the socket, whatever the sender already
        /// checked (D142).
        /// </summary>
        public FirewallStatus Apply(IReadOnlyList<FirewallRule> rules)
        {
            this.EnsureStateDirectory();

            var text = NftRuleset.Render(rules);

            Write(this.pendingPath, text);

            try
            {
                this.Nft("refused the ruleset", "-c", "-f", this.pendingPath);
                this.Nft("could not load the ruleset", "-f", this.pendingPath);
            }
            catch
            {
                // Nothing is kept from an apply that did not happen: the ruleset on the machine
                // and the last.nft a reboot re-applies both stay what they were.
                File.Delete(this.pendingPath);
                throw;
            }

            File.Delete(this.pendingPath);

            var applied = new FirewallStatus
            {
                AppliedAtUtc = DateTimeOffset.UtcNow,
                AppliedRules = rules.ToList(),
            };

            Write(this.rulesetPath, text);
            Write(this.statusPath, HelperJson.Line(applied));
            this.status = applied;

            Log.Info($"Firewall applied: {rules.Count} rule(s) — {Summary(rules)}");

            return applied;
        }

        /// <summary>
        /// What was last applied. Deliberately not a read of the live ruleset: what the web side
        /// compares against its expected list is what this Helper did, and "somebody ran nft by
        /// hand" is not a thing this page pretends to detect (D142).
        /// </summary>
        public FirewallStatus Current() => this.status;

        /// <summary>
        /// Puts the last applied ruleset back after a reboot. Checked before it is loaded, like
        /// any other apply. A failure here is logged and nothing more: a Helper that refuses to
        /// start because an old ruleset no longer parses is a Helper nobody can use to fix it.
        /// </summary>
        public void ReapplyLast()
        {
            this.EnsureStateDirectory();
            this.status = this.ReadStatus();

            if (!File.Exists(this.rulesetPath))
            {
                Log.Info($"No firewall ruleset has been applied on this machine yet ({this.rulesetPath} does not exist).");
                return;
            }

            try
            {
                this.Nft("refused the stored ruleset", "-c", "-f", this.rulesetPath);
                this.Nft("could not load the stored ruleset", "-f", this.rulesetPath);

                Log.Info($"Firewall re-applied from {this.rulesetPath} ({this.status.AppliedRules.Count} rule(s), applied {this.status.AppliedAtUtc:u}).");
            }
            catch (Exception ex)
            {
                Log.Error($"Could not re-apply {this.rulesetPath}; this machine's firewall is whatever nft already had. {ex.Message}");
            }
        }

        /// <summary>The rules as one line of log, so the journal says what was opened and why.</summary>
        private static string Summary(IReadOnlyList<FirewallRule> rules) =>
            rules.Count == 0
                ? "none, so the safety rules only"
                : string.Join(", ", rules.Select(rule => $"{rule.Label} ({rule.Protocol.ToString().ToLowerInvariant()} {rule.PortRange})"));

        /// <summary>0700 root:root, created if the installer has not been near this box yet.</summary>
        private void EnsureStateDirectory()
        {
            Directory.CreateDirectory(this.stateDirectory);
            File.SetUnixFileMode(this.stateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Native.Chown(this.stateDirectory, 0, 0);
        }

        /// <summary>
        /// One nft invocation. <paramref name="failure"/> is how the sentence reads when it does
        /// not work — an admin sees "nft refused the ruleset: ..." with nft's own words after it.
        /// </summary>
        private void Nft(string failure, params string[] arguments)
        {
            if (!File.Exists(NftPath))
                throw new FirewallException($"{NftPath} is not installed, so the firewall cannot be changed. Install the nftables package on this server.");

            var info = new ProcessStartInfo
            {
                FileName = NftPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            // ArgumentList, never a command line: there is no string here for anything to be
            // quoted into or out of, which is the rule the whole Helper exists to keep (D142).
            foreach (var argument in arguments)
                info.ArgumentList.Add(argument);

            using var process = Process.Start(info)
                ?? throw new FirewallException($"Could not run {NftPath}.");

            // Both pipes are read while it runs. nft says very little, but a process blocked on a
            // full pipe while we block on WaitForExit is a Helper that never answers again.
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(NftTimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                throw new FirewallException($"nft did not answer within {NftTimeoutMilliseconds / 1000} seconds; the firewall was left as it was.");
            }

            if (process.ExitCode == 0)
                return;

            var said = error.GetAwaiter().GetResult().Trim();
            if (said.Length == 0)
                said = output.GetAwaiter().GetResult().Trim();
            if (said.Length == 0)
                said = $"it exited with status {process.ExitCode} and said nothing.";

            throw new FirewallException($"nft {failure}: {said}");
        }

        /// <summary>
        /// The status of the last apply, from the file beside the ruleset. Anything unreadable
        /// counts as "nothing has been applied", which is the honest answer when we cannot say
        /// what was.
        /// </summary>
        private FirewallStatus ReadStatus()
        {
            if (!File.Exists(this.statusPath))
                return new FirewallStatus();

            try
            {
                return HelperJson.Parse<FirewallStatus>(File.ReadAllText(this.statusPath)) ?? new FirewallStatus();
            }
            catch (IOException ex)
            {
                Log.Warn($"Could not read {this.statusPath}: {ex.Message}");
                return new FirewallStatus();
            }
        }

        /// <summary>Writes one of our state files 0600, whether or not it was already there.</summary>
        private static void Write(string path, string text)
        {
            File.WriteAllText(path, text);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
