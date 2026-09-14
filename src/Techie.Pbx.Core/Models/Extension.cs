using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// A SIP extension (one PJSIP endpoint + auth + aor).
    /// </summary>
    public partial class Extension
    {
        public long ExtensionID { get; set; }
        public string Number { get; set; } = "";
        public string Name { get; set; } = "";
        public string Secret { get; set; } = "";
        public bool Enabled { get; set; } = true;

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

            return errors;
        }

        [GeneratedRegex(@"^[0-9]{2,6}$")]
        private static partial Regex NumberPattern();

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+$")]
        private static partial Regex NamePattern();

        [GeneratedRegex(@"^[A-Za-z0-9]{16,64}$")]
        private static partial Regex SecretPattern();
    }
}
