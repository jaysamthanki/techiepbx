using System.Text;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders cdr.conf (F5). The CDR engine is built into the core (there is no cdr_core module
    /// to load), reads this file once at startup, and with no file at all runs on defaults. One of
    /// those defaults is <c>unanswered = no</c>: a call nobody picks up is never recorded, and a
    /// missed inbound trunk call — half of the reports' "missed" — would leave no row. The reports
    /// need the opposite, so the file exists to say so.
    ///
    /// Read once at startup: like rtp.conf, it is written here and applied by a restart (D33).
    /// Nothing in it comes from the database, so there are no settings and no validation.
    /// </summary>
    public static class CdrConfRenderer
    {
        public const string FileName = "cdr.conf";

        public static string Render()
        {
            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[general]\n");
            sb.Append("unanswered = yes\n");

            return sb.ToString();
        }
    }
}
