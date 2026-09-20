using System.Net;
using System.Reflection;
using System.Text;

namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// Fills in the alert email template (D114). A pure function — template and data in, HTML out —
    /// like the conf renderers, so it can be tested without a mail server anywhere near it.
    ///
    /// Every value is HTML-encoded on the way in. The template is ours and the values are not, so
    /// this is the one place that decides what may become markup, exactly as
    /// <c>ConfText.Safe</c> is for conf files.
    /// </summary>
    public static class AlertEmailRenderer
    {
        /// <summary>Where the template lives in the assembly, embedded by the csproj.</summary>
        public const string ResourceName = "templates/AlertEmail.html";

        /// <summary>The template as shipped, read from the assembly once and reused.</summary>
        public static string Template { get; } = Load();

        /// <summary>The finished HTML for one alert.</summary>
        public static string Render(AlertEmail alert)
        {
            var html = Template;

            html = Block(html, "Detail", alert.Details.Count > 0);
            html = Block(html, "Button", alert.ButtonUrl.Length > 0 && alert.ButtonText.Length > 0);

            // The accent is one of our own constants rather than anything a caller typed, but it
            // lands inside a style attribute, so it is encoded like everything else.
            html = html.Replace("{{AccentColor}}", Encode(alert.AccentColor), StringComparison.Ordinal);
            html = html.Replace("{{Body}}", Paragraphs(alert.Body), StringComparison.Ordinal);
            html = html.Replace("{{ButtonText}}", Encode(alert.ButtonText), StringComparison.Ordinal);
            html = html.Replace("{{ButtonUrl}}", Encode(alert.ButtonUrl), StringComparison.Ordinal);
            html = html.Replace("{{DetailRows}}", Rows(alert.Details), StringComparison.Ordinal);
            html = html.Replace("{{Hostname}}", Encode(alert.Hostname), StringComparison.Ordinal);
            html = html.Replace("{{Subject}}", Encode(alert.Subject), StringComparison.Ordinal);
            html = html.Replace("{{Timestamp}}", Encode(alert.Timestamp), StringComparison.Ordinal);

            return html;
        }

        /// <summary>
        /// Keeps or drops one of the template's paired optional blocks. Keeping it removes the two
        /// marker lines and leaves the markup between them; dropping it removes the markers and
        /// everything between, so an alert with no details carries no empty table (D114).
        /// </summary>
        private static string Block(string html, string name, bool keep)
        {
            var start = "{{" + name + "BlockStart}}";
            var end = "{{" + name + "BlockEnd}}";

            if (keep)
                return html.Replace(start, "", StringComparison.Ordinal).Replace(end, "", StringComparison.Ordinal);

            var from = html.IndexOf(start, StringComparison.Ordinal);
            var to = html.IndexOf(end, StringComparison.Ordinal);

            // A template that lost a marker is a broken build, not a runtime decision to make.
            if (from < 0 || to < from)
                return html;

            return html.Remove(from, to - from + end.Length);
        }

        private static string Encode(string value) => WebUtility.HtmlEncode(value ?? "");

        private static string Load()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"The email template '{ResourceName}' is not embedded in this build.");

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// The body, written as sentences and rendered as lines. Blank entries become a gap between
        /// paragraphs, which is how a caller asks for one without writing any markup.
        /// </summary>
        private static string Paragraphs(IReadOnlyList<string> body)
        {
            var sb = new StringBuilder();

            foreach (var line in body)
            {
                if (sb.Length > 0)
                    sb.Append("<br />\n");

                sb.Append(line.Trim().Length == 0 ? "<br />" : Encode(line));
            }

            return sb.ToString();
        }

        /// <summary>The detail table's rows, styled inline because email clients strip head CSS.</summary>
        private static string Rows(IReadOnlyList<AlertEmailDetail> details)
        {
            var sb = new StringBuilder();

            foreach (var detail in details)
            {
                sb.Append("<tr>");
                sb.Append("<td style=\"padding:4px 12px 4px 0; color:#6b7280; white-space:nowrap; vertical-align:top;\">")
                  .Append(Encode(detail.Label))
                  .Append("</td>");
                sb.Append("<td style=\"padding:4px 0; color:#374151;\">")
                  .Append(Encode(detail.Value))
                  .Append("</td>");
                sb.Append("</tr>\n");
            }

            return sb.ToString();
        }
    }
}
