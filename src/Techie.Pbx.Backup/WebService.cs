using System.Diagnostics;
using log4net;

namespace Techie.Pbx.Backup
{
    /// <summary>
    /// The web app's systemd unit, as restore needs it: stopped while its database is replaced,
    /// started again after, and asked over HTTP whether it came up. systemctl runs at a fixed
    /// path with a fixed unit and one of two fixed verbs, through an argument list — no shell.
    /// </summary>
    public static class WebService
    {
        /// <summary>The unit app-deploy.sh installs.</summary>
        public const string Unit = "tnpbx-web";

        /// <summary>The web app always listens on 8080 (D99); 127.0.0.1 rather than localhost, which may be ::1 first.</summary>
        private const string Address = "http://127.0.0.1:8080/";

        private const int PingAttempts = 10;

        private const string SystemCtl = "/usr/bin/systemctl";

        private static readonly ILog Log = LogManager.GetLogger(typeof(WebService));

        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(3);

        /// <summary><c>systemctl start|stop tnpbx-web</c>. Throws when systemctl says it failed.</summary>
        public static void Control(string verb)
        {
            if (verb != "start" && verb != "stop")
                throw new ArgumentException($"'{verb}' is not start or stop.", nameof(verb));

            var start = new ProcessStartInfo(SystemCtl)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(verb);
            start.ArgumentList.Add(Unit);

            using var process = Process.Start(start) ?? throw new BackupException($"Could not run {SystemCtl}.");
            var error = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new BackupException($"systemctl {verb} {Unit} failed (exit {process.ExitCode}): {error.Trim()}");
        }

        /// <summary>
        /// Whether the web app answers HTTP on 8080 within about half a minute. Any answer counts —
        /// a redirect to sign in is the app being up.
        /// </summary>
        public static bool IsUp()
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(5),
            };

            for (var attempt = 1; attempt <= PingAttempts; attempt++)
            {
                try
                {
                    using var response = client.GetAsync(Address).GetAwaiter().GetResult();
                    Log.Info($"{Address} answered {(int)response.StatusCode}");
                    return true;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    Log.Info($"{Address} not answering yet (attempt {attempt} of {PingAttempts})");
                    Thread.Sleep(PingInterval);
                }
            }

            return false;
        }
    }
}
