using System.Reflection;
using System.Runtime.InteropServices;
using log4net;
using log4net.Config;
using Techie.Pbx.Contracts;
using Techie.Pbx.Helper.Firewall;

namespace Techie.Pbx.Helper
{
    /// <summary>
    /// The privileged helper, and the only thing on this box that runs as root once the install
    /// is done (D142). It listens on a Unix socket and executes a fixed set of typed messages
    /// from <see cref="Techie.Pbx.Contracts"/>. There is no message that carries a command, a
    /// path or a shell string, and there never will be — that is the whole point of it existing
    /// instead of a sudo rule.
    ///
    /// Startup, in order: resolve the <c>tnpbx</c> user (and refuse to start without it, because
    /// there would be nobody this socket could be for), put the last applied firewall ruleset
    /// back so a reboot comes up protected, then listen.
    ///
    /// Logging is log4net to the console, which systemd puts in the journal.
    /// </summary>
    public class Program
    {
        /// <summary>The web application's account: the one UID allowed to use this socket.</summary>
        public const string OwnerUser = "tnpbx";

        private static readonly ILog Log = LogManager.GetLogger(typeof(Program));

        public static int Main(string[] args)
        {
            var repository = LogManager.GetRepository(Assembly.GetEntryAssembly()!);
            XmlConfigurator.Configure(repository, new FileInfo(Path.Combine(AppContext.BaseDirectory, "log4net.config")));

            Log.Info("TNPBX helper starting");

            if (!OperatingSystem.IsLinux())
            {
                Log.Fatal("This helper only runs on Linux: it uses SO_PEERCRED and nftables.");
                return 1;
            }

            SystemUser? owner;

            try
            {
                owner = Native.LookupUser(OwnerUser);
            }
            catch (Exception ex)
            {
                Log.Fatal($"Could not look up the '{OwnerUser}' user: {ex.Message}");
                return 1;
            }

            if (owner == null)
            {
                // Without it there is no UID to accept and no group to give the socket to, so
                // starting would mean listening for nobody. Refusing says what is wrong instead.
                Log.Fatal($"There is no '{OwnerUser}' user on this system. Run the installer (scripts/install.sh) first.");
                return 1;
            }

            Log.Info($"Serving uid {owner.Uid}, group {owner.Gid} ({owner.Name})");

            var firewall = new NftFirewall();
            firewall.ReapplyLast();

            using var stopping = new CancellationTokenSource();
            using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => Stop(context, stopping));
            using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => Stop(context, stopping));

            try
            {
                new HelperServer(owner, firewall).Listen(stopping.Token);
            }
            catch (Exception ex)
            {
                Log.Fatal($"The helper stopped: {ex}");
                return 1;
            }

            Log.Info($"TNPBX helper stopped ({HelperSocket.Path} removed)");
            return 0;
        }

        /// <summary>
        /// systemd's stop, and Ctrl-C when somebody is running this by hand. The signal is taken
        /// as handled so the runtime does not end the process from under the listener: closing
        /// the socket and removing its file is what a clean stop means here.
        /// </summary>
        private static void Stop(PosixSignalContext context, CancellationTokenSource stopping)
        {
            context.Cancel = true;
            Log.Info($"{context.Signal} received, stopping");
            stopping.Cancel();
        }
    }
}
