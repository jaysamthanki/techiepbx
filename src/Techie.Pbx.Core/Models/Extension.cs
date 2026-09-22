using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// A SIP extension (one PJSIP endpoint + auth + aor), and the optional voicemail box that
    /// belongs to it (D27).
    /// </summary>
    public partial class Extension
    {
        public bool Enabled { get; set; } = true;
        public long ExtensionID { get; set; }
        public string Name { get; set; } = "";
        public string Number { get; set; } = "";

        /// <summary>
        /// What this extension's calls to the outside world present as, or empty for none (D125):
        /// the user with a direct DID of their own calls out as that DID. Either form
        /// <see cref="CallerIDFormat"/> reads, and it beats everything — the endpoint carries it as
        /// <c>set_var = TNPBX_CID=...</c>, so every channel this phone creates claims it before any
        /// outbound route is reached.
        ///
        /// Internal calls are untouched: they show <see cref="Name"/> and <see cref="Number"/> from
        /// the endpoint's own <c>callerid</c>, and nothing internal reads the variable.
        /// </summary>
        public string OutboundCallerID { get; set; } = "";

        public string Secret { get; set; } = "";

        /// <summary>Attach the recording to the email. Only means anything once F4 sends them.</summary>
        public bool VoicemailAttachRecording { get; set; } = true;

        /// <summary>
        /// Delete the message from the mailbox once it has been emailed. Off by default: with it
        /// on, a mail problem loses the message.
        /// </summary>
        public bool VoicemailDeleteAfterEmail { get; set; }

        /// <summary>Where voicemail notifications will go. Nothing sends them yet (F4).</summary>
        public string VoicemailEmail { get; set; } = "";

        public bool VoicemailEnabled { get; set; }

        /// <summary>
        /// What the user types into the phone to hear their messages. Digits only, because that
        /// is all a phone keypad has. Asterisk stores it in voicemail.conf as typed (D28).
        /// </summary>
        public string VoicemailPin { get; set; } = "";

        /// <summary>
        /// Transcribe the recording and put the text in the email (D128). Off by default: it costs
        /// roughly the length of the message in CPU time on the box itself, and it does nothing at
        /// all without an address to email, so the renderer writes it for a mailbox that has one.
        /// </summary>
        public bool VoicemailTranscribe { get; set; }

        /// <summary>
        /// What counts as an extension number. Public because a destination points at one, and
        /// two places deciding what a number looks like is one place too many.
        /// </summary>
        public static bool IsValidNumber(string number) => NumberPattern().IsMatch(number);

        /// <summary>
        /// Returns a list of problems; empty means valid. These values end up in Asterisk
        /// config files, so the rules are deliberately strict.
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (!NumberPattern().IsMatch(Number))
                errors.Add("Number must be 2 to 6 digits.");

            if (string.IsNullOrWhiteSpace(Name))
                errors.Add("Name is required.");
            else if (Name.Length > 64)
                errors.Add("Name must be 64 characters or fewer.");
            else if (!NamePattern().IsMatch(Name))
                errors.Add("Name may only contain letters, digits, spaces and . , ' - _ ( ) &");

            if (!SecretPattern().IsMatch(Secret))
                errors.Add("Secret must be 16 to 64 letters or digits.");

            // The caller ID this extension calls out as, if it has one of its own (D125). It is
            // written into pjsip.conf as a channel variable and read by the outbound route contexts,
            // so it is checked here the way everything that reaches a conf file is.
            var outboundCallerID = CallerIDFormat.Error(OutboundCallerID, "Outbound caller ID");
            if (outboundCallerID != null)
                errors.Add(outboundCallerID);

            // The PIN only has to be there when there is a mailbox to unlock; the other voicemail
            // settings are kept whether the mailbox is on or off, so a switched-off box that is
            // switched back on is the one it was.
            if (VoicemailEnabled && !VoicemailPinPattern().IsMatch(VoicemailPin))
                errors.Add("Voicemail PIN must be 4 to 8 digits.");

            if (VoicemailEmail.Length > 0)
            {
                if (VoicemailEmail.Length > 128)
                    errors.Add("Voicemail email must be 128 characters or fewer.");
                else if (!EmailPattern().IsMatch(VoicemailEmail))
                    errors.Add("Voicemail email must be an email address, e.g. name@example.com.");
            }

            return errors;
        }

        [GeneratedRegex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$")]
        private static partial Regex EmailPattern();

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+$")]
        private static partial Regex NamePattern();

        [GeneratedRegex(@"^[0-9]{2,6}$")]
        private static partial Regex NumberPattern();

        [GeneratedRegex(@"^[A-Za-z0-9]{16,64}$")]
        private static partial Regex SecretPattern();

        [GeneratedRegex(@"^[0-9]{4,8}$")]
        private static partial Regex VoicemailPinPattern();
    }
}
