using System.Text;
using System.Text.RegularExpressions;

namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// An action to send to Asterisk: an action name plus header lines.
    /// </summary>
    public partial class AmiAction
    {
        private readonly List<KeyValuePair<string, string>> _headers = new();

        public string Name { get; }

        public AmiAction(string name)
        {
            Name = SafeName(name, "action name");
        }

        public AmiAction Add(string name, string value)
        {
            _headers.Add(new KeyValuePair<string, string>(SafeName(name, "header name"), SafeValue(value, name)));
            return this;
        }

        /// <summary>
        /// The exact text that goes on the wire, including the ActionID and the blank line
        /// that ends the packet.
        /// </summary>
        public string ToProtocol(string actionID)
        {
            var sb = new StringBuilder();
            sb.Append("Action: ").Append(Name).Append("\r\n");
            sb.Append("ActionID: ").Append(SafeName(actionID, "ActionID")).Append("\r\n");

            foreach (var header in _headers)
                sb.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");

            sb.Append("\r\n");
            return sb.ToString();
        }

        /// <summary>
        /// AMI is a line protocol, so a CR or LF in a value would let a caller append a second
        /// action of their choosing. Same idea as ConfText.Safe for config files: refuse rather
        /// than escape.
        /// </summary>
        private static string SafeValue(string value, string field)
        {
            if (value.Any(c => c is '\r' or '\n' or '\0' || char.IsControl(c)))
                throw new InvalidOperationException($"Refusing to send an unsafe value for '{field}' over AMI.");
            return value;
        }

        private static string SafeName(string name, string field)
        {
            if (!NamePattern().IsMatch(name))
                throw new InvalidOperationException($"Refusing to send an unsafe {field} over AMI.");
            return name;
        }

        [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}$")]
        private static partial Regex NamePattern();
    }
}
