using System.ComponentModel;
using System.Diagnostics;
using log4net;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Restarts Asterisk, for the files it only reads when it starts (D33, D104).
    ///
    /// The mechanism is the one the installer set up: a polkit rule lets the <c>tnpbx</c> user
    /// manage exactly <c>asterisk.service</c> and nothing else, so <c>systemctl</c> is run directly
    /// as the web user. **No sudo, and no shell** (D3, D18): a fixed program and a fixed argv list,
    /// with nothing in it that came from a user, the database or a request
    /// (security.md: "never shells out with string-built commands").
    ///
    /// Restarting drops live calls, which is why nothing here decides to do it: the caller asks
    /// the admin first (D104).
    /// </summary>
    public static class AsteriskRestart
    {
        /// <summary>The one unit the polkit rule allows, spelled the way the rule matches it.</summary>
        public const string Unit = "asterisk.service";

        /// <summary>
        /// Long enough for Asterisk to stop and come back on a small VM, short enough that a
        /// systemd job that never finishes does not hold the request open.
        /// </summary>
        public const int TimeoutSeconds = 30;

        private const string Program = "systemctl";

        private static readonly ILog Log = LogManager.GetLogger(typeof(AsteriskRestart));

        /// <summary>
        /// Runs <c>systemctl restart asterisk.service</c> and waits for it. Returns what happened
        /// rather than throwing: every way this can fail — polkit refusing, systemd refusing,
        /// Asterisk failing to come back — is something the admin is shown, not an error page.
        /// </summary>
        public static RestartResult Run()
        {
            var start = new ProcessStartInfo
            {
                FileName = Program,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            start.ArgumentList.Add("restart");
            start.ArgumentList.Add(Unit);

            Process process;

            try
            {
                process = Process.Start(start) ?? throw new Win32Exception("systemctl did not start.");
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
            {
                Log.Error($"Could not run '{Program} restart {Unit}': {ex.Message}", ex);
                return new RestartResult(false, $"{Program} could not be run on this server, so Asterisk was not restarted.");
            }

            using (process)
            {
                // Read the pipes while it runs, so a process that fills one cannot block on it.
                var error = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEndAsync();

                if (!process.WaitForExit(TimeoutSeconds * 1000))
                {
                    Kill(process);
                    Log.Error($"Restarting {Unit} took longer than {TimeoutSeconds} seconds and was given up on");
                    return new RestartResult(false,
                        $"Asterisk did not restart within {TimeoutSeconds} seconds. Check the service on the server before trying again.");
                }

                // The overload without a timeout is what flushes the redirected pipes.
                process.WaitForExit();
                Task.WaitAll(error, output);

                if (process.ExitCode == 0)
                {
                    Log.Info($"Restarted {Unit}");
                    return new RestartResult(true, "Asterisk restarted.");
                }

                Log.Error($"{Program} restart {Unit} exited {process.ExitCode}: {Trimmed(error.Result)}");
                return new RestartResult(false,
                    $"Asterisk could not be restarted ({Program} exited {process.ExitCode}). The server's log has what it said.");
            }
        }

        private static void Kill(Process process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                Log.Warn($"Could not stop {Program}: {ex.Message}");
            }
        }

        /// <summary>The log wants the gist of what systemd said, not a screen of it.</summary>
        private static string Trimmed(string error)
        {
            var text = error.ReplaceLineEndings(" ").Trim();
            return text.Length <= 500 ? text : text[..500] + "…";
        }
    }
}
