using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// A SIP extension (one PJSIP endpoint + auth + aor), and the optional voicemail box that
    /// belongs to it (D27).
    /// </summary>
    public partial class Extension
    {
        /// <summary>
        /// The longest the forwarding field may be, which is four 15-digit numbers and the spaces
        /// between them. A cap on the raw text as well as on the tokens, so a pasted essay is one
        /// tidy error rather than an error quoting the essay back.
        /// </summary>
        public const int MaxForwardingLength = 64;

        /// <summary>
        /// How many places one extension may ring at once (D130). Four is more phones than anyone
        /// has and few enough that a mistyped list cannot become a broadcast.
        /// </summary>
        public const int MaxForwardingTargets = 4;

        public bool Enabled { get; set; } = true;
        public long ExtensionID { get; set; }

        /// <summary>
        /// Where a call to this extension rings, or empty for the extension's own phone (D130).
        /// Space separated, and it <b>replaces</b> the phone rather than being tried after it: the
        /// field is the whole ring, so somebody who wants their own handset to keep ringing puts
        /// their own <see cref="Number"/> in the list.
        ///
        /// Each token is either the <see cref="Number"/> of an extension or a number to dial out.
        /// Which one it is, the renderer decides by looking: an extension becomes its endpoint, and
        /// anything else becomes a <c>Local</c> channel into the context a phone dials from, so the
        /// outbound routes and their toll rules are the ones a manually dialled call gets.
        ///
        /// The shape is checked here; whether a token that looks like an extension number really is
        /// one needs the other rows, so <c>ExtensionRepository</c> checks that.
        /// </summary>
        public string Forwarding { get; set; } = "";

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
        /// What is wrong with the forwarding field, if anything (D130). Its own method because it
        /// is four rules rather than one, and because every one of them is about a value that ends
        /// up inside a <c>Dial</c>.
        /// </summary>
        private List<string> ForwardingErrors()
        {
            var errors = new List<string>();

            if (this.Forwarding.Length > MaxForwardingLength)
            {
                errors.Add($"Forwarding must be {MaxForwardingLength} characters or fewer.");
                return errors;
            }

            var targets = this.ForwardingList();

            if (targets.Count > MaxForwardingTargets)
                errors.Add($"Forwarding may ring at most {MaxForwardingTargets} places at once.");

            foreach (var target in targets.Where(t => !IsForwardingTarget(t)))
            {
                // The leading zero gets its own message, because it is the one refusal here that
                // is about money rather than about typing (D47, D130).
                if (target.Length > 1 && target[0] == OutboundRoute.InternationalPrefix && target.All(char.IsAsciiDigit))
                    errors.Add($"Forwarding to '{target}' is refused: a number may not start with 0, because 00 and 011 are international dialling.");
                else
                    errors.Add($"'{target}' is not somewhere this system can ring. Forwarding takes extension numbers and full phone numbers, 2 to 15 digits each, separated by spaces.");
            }

            if (targets.Count != targets.Distinct(StringComparer.Ordinal).Count())
                errors.Add("Forwarding can only ring the same place once.");

            return errors;
        }

        /// <summary>
        /// The forwarding targets, in order, as the list the renderer rings (D130). Split on
        /// whitespace and nothing else: a comma is not a separator here, so "103,7146085242"
        /// is one token, fails the shape check, and is named in the error rather than being
        /// quietly read as two numbers.
        /// </summary>
        public List<string> ForwardingList() =>
            this.Forwarding.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

        /// <summary>
        /// Whether this is something a forwarding list may ring: digits only, 2 to 15 of them, and
        /// never starting with 0 (D130).
        ///
        /// One pattern for both kinds of token, because both end up in the same Dial. It is wide
        /// enough for any extension number (<see cref="IsValidNumber"/>) and for an external number
        /// up to E.164's own limit, and the leading digit is the outbound routes' toll-fraud rule
        /// arriving from the other side: 00 and 011 are international dialling, so a number
        /// starting with 0 is refused rather than guessed about (<see cref="OutboundRoute.InternationalPrefix"/>).
        /// </summary>
        public static bool IsForwardingTarget(string target) => ForwardingTargetPattern().IsMatch(target);

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

            // Where calls to this extension ring, if it is not this extension's own phone (D130).
            // Everything here is about the shape of the list; the repository decides whether a
            // token that looks like an extension number is one, because only it can see.
            errors.AddRange(this.ForwardingErrors());

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

        [GeneratedRegex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\z")]
        private static partial Regex EmailPattern();

        /// <summary>2 to 15 digits, the first of which is not a 0.</summary>
        [GeneratedRegex(@"^[1-9][0-9]{1,14}\z")]
        private static partial Regex ForwardingTargetPattern();

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex NamePattern();

        [GeneratedRegex(@"^[0-9]{2,6}\z")]
        private static partial Regex NumberPattern();

        [GeneratedRegex(@"^[A-Za-z0-9]{16,64}\z")]
        private static partial Regex SecretPattern();

        [GeneratedRegex(@"^[0-9]{4,8}\z")]
        private static partial Regex VoicemailPinPattern();
    }
}
