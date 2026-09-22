using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;

namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// Fills in the voicemail email template (D129), the same way <see cref="AlertEmailRenderer"/>
    /// fills in the alert one (D114): a pure function — template and data in, HTML out — so it can
    /// be tested without a mail server, a mailbox or a recording anywhere near it.
    ///
    /// Every value is HTML-encoded on the way in, and here that is not a formality: the caller ID
    /// name on an inbound call is a string a stranger chose, and this is the one place that decides
    /// what may become markup — exactly as <c>ConfText.Safe</c> is for conf files.
    /// </summary>
    public static class VoicemailEmailRenderer
    {
        /// <summary>What the subject and the body say when the caller withheld everything.</summary>
        public const string UnknownCaller = "an unknown caller";

        /// <summary>Where the template lives in the assembly, embedded by the csproj.</summary>
        public const string ResourceName = "templates/VoicemailEmail.html";

        /// <summary>The template as shipped, read from the assembly once and reused.</summary>
        public static string Template { get; } = Load();

        /// <summary>
        /// Who the message is from, in one phrase: the name and the number when the trunk sent
        /// both, whichever there is when there is only one, and <see cref="UnknownCaller"/> when
        /// there is neither. Public because the subject line says the same thing the body does.
        /// </summary>
        public static string Caller(VoicemailEmail voicemail)
        {
            var name = (voicemail.CallerName ?? "").Trim();
            var number = (voicemail.CallerId ?? "").Trim();

            if (name.Length > 0 && number.Length > 0 && !string.Equals(name, number, StringComparison.Ordinal))
                return $"{name} ({number})";

            if (number.Length > 0)
                return number;

            return name.Length > 0 ? name : UnknownCaller;
        }

        /// <summary>
        /// The length as a listener reads it: minutes and seconds, so a two minute message is
        /// "2:05" rather than "125 seconds". Anything negative is a count nobody sent.
        /// </summary>
        public static string Duration(int seconds)
        {
            var total = seconds < 0 ? 0 : seconds;

            var minutes = (total / 60).ToString(CultureInfo.InvariantCulture);
            var remainder = (total % 60).ToString("00", CultureInfo.InvariantCulture);

            return $"{minutes}:{remainder}";
        }

        /// <summary>The finished HTML for one voicemail.</summary>
        public static string Render(VoicemailEmail voicemail)
        {
            var transcript = (voicemail.Transcript ?? "").Trim();
            var html = Template;

            html = Block(html, "Attachment", voicemail.RecordingAttached);
            html = Block(html, "Transcript", transcript.Length > 0);

            html = html.Replace("{{Caller}}", Encode(Caller(voicemail)), StringComparison.Ordinal);
            html = html.Replace("{{Duration}}", Encode(Duration(voicemail.DurationSeconds)), StringComparison.Ordinal);
            html = html.Replace("{{Hostname}}", Encode(voicemail.Hostname), StringComparison.Ordinal);
            html = html.Replace("{{Mailbox}}", Encode(voicemail.Mailbox), StringComparison.Ordinal);
            html = html.Replace("{{MailboxName}}", Encode(voicemail.MailboxName), StringComparison.Ordinal);
            html = html.Replace("{{Received}}", Encode(Received(voicemail.ReceivedAt)), StringComparison.Ordinal);
            html = html.Replace("{{Subject}}", Encode(Subject(voicemail)), StringComparison.Ordinal);
            html = html.Replace("{{Transcript}}", Paragraphs(transcript), StringComparison.Ordinal);

            return html;
        }

        /// <summary>
        /// The subject line: who it is from and which mailbox it landed in, which is the same
        /// shape app_voicemail's own subject had (D126) so a mail rule written for one still
        /// matches the other. Public because the sender puts it on the message.
        /// </summary>
        public static string Subject(VoicemailEmail voicemail) =>
            $"New voicemail from {Caller(voicemail)} in mailbox {voicemail.Mailbox}";

        /// <summary>
        /// Keeps or drops one of the template's paired optional blocks, exactly as the alert
        /// renderer does: keeping it removes the two marker lines, dropping it removes the markers
        /// and everything between them.
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
        /// The transcript as lines. whisper returns one block of speech, but a model that decides
        /// to break it into lines should not lose them to HTML's idea of whitespace.
        /// </summary>
        private static string Paragraphs(string transcript)
        {
            var sb = new StringBuilder();

            foreach (var line in transcript.ReplaceLineEndings("\n").Split('\n'))
            {
                if (sb.Length > 0)
                    sb.Append("<br />\n");

                sb.Append(Encode(line.Trim()));
            }

            return sb.ToString();
        }

        /// <summary>When it arrived, written for a reader rather than for a machine.</summary>
        private static string Received(DateTimeOffset receivedAt) =>
            receivedAt.ToString("dddd d MMMM yyyy, HH:mm");
    }
}
