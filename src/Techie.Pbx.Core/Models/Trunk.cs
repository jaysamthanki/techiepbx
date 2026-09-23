using System.Text.RegularExpressions;
using Techie.Pbx.Core.Security;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// A connection to a SIP provider: one PJSIP endpoint + auth + aor, optionally a registration,
    /// optionally an identify. Everything here ends up in pjsip.conf, so the rules are strict.
    /// </summary>
    public partial class Trunk
    {
        /// <summary>
        /// The codecs a trunk may be configured with: exactly the ones modules.conf loads (D31).
        /// Offering a codec Asterisk has no module for would render config that cannot work. The
        /// list itself is <see cref="SipCodecs.Allowed"/>, shared with the extensions' codec
        /// setting so there is one answer to "which codecs exist here" (D73).
        /// </summary>
        public static readonly IReadOnlyList<string> AllowedCodecs = SipCodecs.Allowed;

        /// <summary>The username to authenticate with, when the provider's differs from Username.</summary>
        public string AuthUsername { get; set; } = "";

        public string CallerIDName { get; set; } = "";
        public string CallerIDNumber { get; set; } = "";

        /// <summary>Comma separated, in preference order, e.g. "ulaw,alaw".</summary>
        public string Codecs { get; set; } = SipCodecs.Default;

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Comma separated IPv4 addresses or CIDR ranges the provider sends calls from. Without
        /// at least one, Asterisk cannot tell an inbound call from this provider apart from a
        /// stranger's, and will refuse it.
        /// </summary>
        public string MatchAddresses { get; set; } = "";

        /// <summary>
        /// Becomes the PJSIP section name and the inbound dialplan context, so it starts with a
        /// letter: an all-digit name could collide with an extension's sections (D37).
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>The password the provider issued. Never rendered anywhere but pjsip.conf.</summary>
        public string Password { get; set; } = "";

        /// <summary>Whether we register with the provider, i.e. tell it where to send calls.</summary>
        public bool Register { get; set; } = true;

        public string ServerHost { get; set; } = "";
        public int ServerPort { get; set; } = 5060;
        public long TrunkID { get; set; }
        public string Username { get; set; } = "";

        /// <summary>
        /// The dialplan context inbound calls from this trunk arrive in. One per trunk, so an
        /// inbound route can tell which provider a call came from (D38).
        /// </summary>
        public string Context => $"from-trunk-{this.Name}";

        /// <summary>What goes in the auth section: the provider's auth username, or the username.</summary>
        public string EffectiveAuthUsername => this.AuthUsername.Length > 0 ? this.AuthUsername : this.Username;

        /// <summary>The codecs as a list, in order, ignoring blanks.</summary>
        public List<string> CodecList() =>
            this.Codecs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        /// <summary>The provider addresses as a list, in order, ignoring blanks.</summary>
        public List<string> MatchAddressList() =>
            this.MatchAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (!NamePattern().IsMatch(Name))
                errors.Add("Name must start with a letter and contain only letters, digits and dashes (up to 32 characters).");

            if (string.IsNullOrWhiteSpace(ServerHost))
                errors.Add("Server host is required.");
            else if (!HostPattern().IsMatch(ServerHost))
                errors.Add("Server host must be a hostname or IP address.");

            if (ServerPort is < 1 or > 65535)
                errors.Add("Server port must be between 1 and 65535.");

            if (Username.Length > 128 || (Username.Length > 0 && !UserPattern().IsMatch(Username)))
                errors.Add("Username may only contain letters, digits and . _ - + @ (up to 128 characters).");

            if (AuthUsername.Length > 128 || (AuthUsername.Length > 0 && !UserPattern().IsMatch(AuthUsername)))
                errors.Add("Auth username may only contain letters, digits and . _ - + @ (up to 128 characters).");

            if (Password.Length > 128 || (Password.Length > 0 && !PasswordPattern().IsMatch(Password)))
                errors.Add("Password must be 128 characters or fewer and may not contain ; [ ] \" or spaces.");

            // Registering is telling the provider where to send calls, which it will only accept
            // from someone who can prove who they are.
            if (Register && Username.Length == 0)
                errors.Add("A trunk that registers needs a username.");

            if (Register && Password.Length == 0)
                errors.Add("A trunk that registers needs a password.");

            if (Password.Length > 0 && EffectiveAuthUsername.Length == 0)
                errors.Add("A password needs a username to go with it.");

            ValidateCodecs(errors);
            ValidateMatchAddresses(errors);

            if (CallerIDName.Length > 64 || (CallerIDName.Length > 0 && !NamePartPattern().IsMatch(CallerIDName)))
                errors.Add("Caller ID name may only contain letters, digits, spaces and . , ' - _ ( ) & (up to 64 characters).");

            if (CallerIDNumber.Length > 0 && !CallerIDNumberPattern().IsMatch(CallerIDNumber))
                errors.Add("Caller ID number must be 2 to 20 digits, optionally starting with +.");

            return errors;
        }

        private void ValidateCodecs(List<string> errors)
        {
            var codecs = CodecList();

            if (codecs.Count == 0)
            {
                errors.Add("At least one codec is required.");
                return;
            }

            foreach (var codec in codecs.Where(c => !AllowedCodecs.Contains(c, StringComparer.Ordinal)))
                errors.Add($"'{codec}' is not a codec this system loads. Use {string.Join(", ", AllowedCodecs)}.");
        }

        private void ValidateMatchAddresses(List<string> errors)
        {
            foreach (var address in MatchAddressList())
            {
                if (!Ipv4Networks.TryParse(address, out _))
                    errors.Add($"'{address}' is not an IPv4 address or CIDR range.");
            }
        }

        [GeneratedRegex(@"^\+?[0-9]{2,20}\z")]
        private static partial Regex CallerIDNumberPattern();

        [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9.\-]{0,253}[A-Za-z0-9])?\z")]
        private static partial Regex HostPattern();

        [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9\-]{0,31}\z")]
        private static partial Regex NamePattern();

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex NamePartPattern();

        [GeneratedRegex(@"^[^\s;\[\]""\p{C}]+$")]
        private static partial Regex PasswordPattern();

        [GeneratedRegex(@"^[A-Za-z0-9._\-+@]+\z")]
        private static partial Regex UserPattern();
    }
}
