using System.Globalization;
using System.Text.RegularExpressions;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Where app_voicemail keeps its messages, and — the part that matters — what this application
    /// is willing to believe when something else names one of them (D129).
    ///
    /// The <c>voicemail-mail</c> script posts the path of the message it is about to relay, and
    /// that is a path arriving over HTTP. So it is not opened because it looks plausible: it has to
    /// be exactly the shape app_voicemail writes — the spool root, a context, the mailbox the
    /// request is about, <c>INBOX</c>, and <c>msgNNNN</c> with no extension — and nothing else is
    /// read at all. That rules out traversal, absolute paths elsewhere, and one mailbox asking for
    /// another mailbox's messages, which is the interesting one: the mailbox in the path and the
    /// mailbox in the request have to be the same.
    ///
    /// Pure functions over strings; nothing here touches a disk.
    /// </summary>
    public static partial class VoicemailSpool
    {
        /// <summary>The folder a new message lands in. Nothing else is ever emailed.</summary>
        public const string Folder = "INBOX";

        /// <summary>
        /// Asterisk's voicemail spool, at the built-in default. A constant rather than a setting:
        /// asterisk.conf leaves the directories where they are (D103), and a configurable root
        /// here would be a configurable root for a path check.
        /// </summary>
        public const string Root = "/var/spool/asterisk/voicemail";

        /// <summary>
        /// Where one message's files live, without the extension: the recording is that path plus
        /// <c>.WAV</c> or <c>.g722</c>, and the metadata is that path plus <c>.txt</c>.
        /// </summary>
        public static string Message(string context, string mailbox, int messageNumber) =>
            $"{Root}/{context}/{mailbox}/{Folder}/msg{messageNumber.ToString("0000", CultureInfo.InvariantCulture)}";

        /// <summary>
        /// Why this path may not be opened for this mailbox, or null when it may. A sentence
        /// rather than a bool because it goes into the log line that says what was refused —
        /// never back to the caller, which is a script and does not need telling twice.
        /// </summary>
        public static string? Problem(string mailbox, string? messagePath)
        {
            // Deliberately not trimmed. A path with a space or a newline on the end of it is not a
            // path with untidy whitespace, it is not the path app_voicemail wrote.
            var path = messagePath ?? "";

            if (path.Length == 0)
                return "no message path was given";

            if (path.Length > 256)
                return "the message path is longer than any real one";

            var match = MessagePathPattern().Match(path);

            if (!match.Success)
                return "the message path is not one app_voicemail writes";

            // The one check the pattern cannot make: a well-formed path into somebody else's
            // mailbox is still somebody else's mailbox.
            if (!string.Equals(match.Groups["mailbox"].Value, mailbox, StringComparison.Ordinal))
                return $"the message path belongs to mailbox {match.Groups["mailbox"].Value}, not {mailbox}";

            return null;
        }

        /// <summary>
        /// The only path shape this application opens. Anchored with <c>\A</c> and <c>\z</c>
        /// rather than <c>^</c> and <c>$</c> on purpose: <c>$</c> also matches before a trailing
        /// newline, which would let "msg0000\n" through. No dots and no wildcards anywhere in it,
        /// so there is nothing to normalise and nothing to escape.
        /// </summary>
        [GeneratedRegex(@"\A" + Root + "/(?<context>[A-Za-z0-9_-]{1,64})/(?<mailbox>[0-9]{2,6})/" + Folder + @"/msg[0-9]{4}\z")]
        private static partial Regex MessagePathPattern();
    }
}
