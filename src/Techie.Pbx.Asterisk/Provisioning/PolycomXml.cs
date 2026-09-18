using System.Text;
using Techie.Pbx.Asterisk.Config;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// The little bit of XML writing the two provisioning renderers share. A Polycom config file is
    /// attributes with dotted parameter names hung off grouping elements, so everything here is
    /// about getting a value safely into a quoted attribute.
    ///
    /// Every value that came from the database or from the request goes through
    /// <see cref="Value"/>, which is <see cref="ConfText.Safe"/> — the same last line of defence
    /// the Asterisk conf renderers use — followed by XML escaping. Safe already refuses the double
    /// quote that would end an attribute; the escaping is what stops an ampersand in somebody's
    /// name from making the file unparseable to the phone.
    /// </summary>
    internal static class PolycomXml
    {
        public const string Declaration = "<?xml version=\"1.0\" standalone=\"yes\"?>";

        /// <summary>
        /// One attribute whose value came from the database or from the request, checked and
        /// escaped. This is the one to use unless the value is a literal in our own source.
        /// </summary>
        public static (string Name, string Value) Attribute(string name, string value, string field) =>
            (name, Value(value, field));

        /// <summary>
        /// One attribute whose value is a constant of ours or a number we worked out. It skips
        /// <see cref="ConfText.Safe"/>, because some of these legitimately contain characters a
        /// stored value may not — the dial plan's <c>[2-9]</c> is the reason this exists.
        ///
        /// Never call this with anything that came from a user, a database row or a request.
        /// </summary>
        public static (string Name, string Value) Constant(string name, string value)
        {
            if (value.Contains('"') || value.Any(char.IsControl))
                throw new InvalidOperationException($"Refusing to write '{name}' into a phone config: a constant contains a quote or a control character.");

            return (name, value);
        }

        /// <summary>
        /// A comment line. The text is ours, never a stored value, so it is checked for the one
        /// sequence that could end a comment early rather than escaped.
        /// </summary>
        public static void Comment(StringBuilder sb, string indent, string text)
        {
            if (text.Contains("--", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to write a comment containing '--' into a phone config.");

            sb.Append(indent).Append("<!-- ").Append(text).Append(" -->\n");
        }

        /// <summary>
        /// One element, with each attribute on a line of its own. Long dotted parameter names on
        /// one line would be unreadable, and one attribute per line makes a change to a generated
        /// file a one-line diff.
        /// </summary>
        public static void Element(StringBuilder sb, string indent, string name, IEnumerable<(string Name, string Value)> attributes)
        {
            sb.Append(indent).Append('<').Append(name).Append('\n');

            foreach (var (attribute, value) in attributes)
                sb.Append(indent).Append("    ").Append(attribute).Append("=\"").Append(value).Append("\"\n");

            sb.Append(indent).Append("/>\n");
        }

        /// <summary>A value fit to sit inside a double-quoted XML attribute.</summary>
        public static string Value(string value, string field) =>
            ConfText.Safe(value, field)
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}
