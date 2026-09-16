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
