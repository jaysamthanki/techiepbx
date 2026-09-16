using System.Text;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders asterisk.conf: the handful of core options we ship. Directories are left to the
    /// compiled-in defaults, because we install Asterisk from source to those defaults.
    ///
    /// Asterisk reads this file once, at startup, and nothing re-reads it: a change here needs a
    /// restart, which is why the file carries no module to reload (D33).
    /// </summary>
    public static class AsteriskConfRenderer
    {
        /// <summary>How chatty the console is. 3 is enough to follow a call without drowning.</summary>
        private const int Verbose = 3;

        public static string Render()
        {
            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[options]\n");
            sb.Append($"verbose = {Verbose}\n");

            // Both default to no in current Asterisk; written down because they are the two
            // settings that would turn a dialplan bug into shell access on this box.
            sb.Append("live_dangerously = no\n");
            sb.Append("execincludes = no\n");

            return sb.ToString();
        }
    }
}
