using System.Reflection;
using log4net;
using log4net.Config;

namespace Techie.Pbx.Backup
{
    /// <summary>
    /// <c>tnpbx</c>, the operator's backup and restore tool (D171). Run as root, from cron or a
    /// shell; there is no web side to it. Two verbs:
    ///
    /// <c>tnpbx backup [--full] [-o file.tar.gz]</c> writes a backup, by default into
    /// /opt/tnpbx/backups and then keeps the newest seven there.
    ///
    /// <c>tnpbx restore file.tar.gz</c> puts one back (see <see cref="Restorer"/>).
    ///
    /// Logging is log4net to the console: the console is the report.
    /// </summary>
    public class Program
    {
        private const string Usage =
            "usage: tnpbx backup [--full] [-o <file.tar.gz>]\n" +
            "       tnpbx restore <file.tar.gz>";

        private static readonly ILog Log = LogManager.GetLogger(typeof(Program));

        public static int Main(string[] args)
        {
            var repository = LogManager.GetRepository(Assembly.GetEntryAssembly()!);
            XmlConfigurator.Configure(repository, new FileInfo(Path.Combine(AppContext.BaseDirectory, "log4net.config")));

            if (!OperatingSystem.IsLinux())
            {
                Log.Fatal("tnpbx only runs on Linux.");
                return 1;
            }

            try
            {
                return args.FirstOrDefault() switch
                {
                    "backup" => Backup(args[1..]),
                    "restore" => Restore(args[1..]),
                    "help" or "-h" or "--help" => PrintUsage(0),
                    _ => PrintUsage(2),
                };
            }
            catch (BackupException ex)
            {
                Log.Error(ex.Message);
                return 1;
            }
            catch (Exception ex)
            {
                Log.Fatal($"tnpbx failed: {ex}");
                return 1;
            }
        }

        private static int Backup(string[] args)
        {
            var full = false;
            string? output = null;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--full":
                        full = true;
                        break;

                    case "-o" when i + 1 < args.Length && output == null:
                        output = args[++i];
                        break;

                    default:
                        return PrintUsage(2);
                }
            }

            var layout = InstallLayout.Read(InstallLayout.AppSettingsPath);
            var created = DateTime.UtcNow;
            var writer = new BackupWriter(layout);

            // A -o backup is the operator's to keep: retention only runs on the default folder.
            if (output != null)
                writer.Write(output, full, created);
            else
                writer.WriteDefault(full, created);

            return 0;
        }

        private static int PrintUsage(int exitCode)
        {
            Console.Error.WriteLine(Usage);
            return exitCode;
        }

        private static int Restore(string[] args)
        {
            if (args.Length != 1 || args[0].StartsWith('-'))
                return PrintUsage(2);

            if (Native.EffectiveUid() != 0)
            {
                Log.Error("tnpbx restore must run as root: it stops the web app and replaces files owned by tnpbx and asterisk. Run it with sudo or as root.");
                return 1;
            }

            var layout = InstallLayout.Read(InstallLayout.AppSettingsPath);
            return new Restorer(layout).Run(args[0]) ? 0 : 1;
        }
    }
}
