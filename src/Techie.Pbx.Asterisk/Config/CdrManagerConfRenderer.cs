using System.Text;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders cdr_manager.conf (F5). cdr_manager will not load without this file: with it missing
    /// it logs "Module not activated" and declines, and with no <c>enabled = yes</c> it loads and
    /// sends nothing. There is nothing in it that comes from the database.
    ///
    /// The mappings add two fields the standard Cdr event does not carry. LinkedID ties together
    /// the records of one call. Sequence is the CDR engine's own counter for each record, which with
    /// UniqueID is what identifies one: a ring-all Dial writes a record per phone, all with the
    /// caller's UniqueID. Each mapping is written into the event as <c>${CDR(name)}</c>, which is
    /// why func_cdr is on the modules allowlist.
    ///
    /// cdr.conf IS generated (CdrConfRenderer): the CDR engine's defaults would drop unanswered
    /// calls, and the reports count a missed inbound call (F5).
    /// </summary>
    public static class CdrManagerConfRenderer
    {
        public const string FileName = "cdr_manager.conf";

        public static string Render()
        {
            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[general]\n");
            sb.Append("enabled = yes\n");

            sb.Append('\n');
            sb.Append("[mappings]\n");
            sb.Append("linkedid => LinkedID\n");
            sb.Append("sequence => Sequence\n");

            return sb.ToString();
        }
    }
}
