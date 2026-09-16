using System.Text;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders logger.conf: the console, one message log and a separate security log. The security
    /// events are what fail2ban reads now and what our own blocker will read later (D7), so they
    /// get a file of their own as well as a place in messages.log.
    /// </summary>
    public static class LoggerConfRenderer
    {
        /// <summary>Failed authentications and the like, in a file nothing else writes to.</summary>
        public const string SecurityLogFile = "security.log";

        public static string Render()
        {
            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[general]\n");
            sb.Append("dateformat = %F %T.%3q\n");

            sb.Append('\n');
            sb.Append("[logfiles]\n");
            sb.Append("console => notice,warning,error\n");
            sb.Append("messages.log => notice,warning,error,security\n");
            sb.Append($"{SecurityLogFile} => security\n");

            return sb.ToString();
        }
    }
}
