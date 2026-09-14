using System.Reflection;
using log4net;
using log4net.Config;

namespace Techie.Pbx.Helper
{
    /// <summary>
    /// Privileged helper. Will listen on a root-owned Unix socket and execute a small,
    /// fixed allowlist of typed commands (see Techie.Pbx.Contracts) for the web process.
    /// </summary>
    public class Program
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(Program));

        public static int Main(string[] args)
        {
            var repository = LogManager.GetRepository(Assembly.GetEntryAssembly()!);
            XmlConfigurator.Configure(repository, new FileInfo(Path.Combine(AppContext.BaseDirectory, "log4net.config")));

            Log.Info("TNPBX helper starting");

            // TODO: socket listener + command dispatch.
            return 0;
        }
    }
}
