namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// The two values a yes/no setting may hold. A pair of words rather than a bool because the
    /// Settings table stores strings and the settings form already knows how to offer a fixed list
    /// as a dropdown (D75) — so a switch costs no new widget, no new column type and no third
    /// state that means "somebody typed something odd".
    ///
    /// Blank is still "not set" here as everywhere else in this table, which means the default the
    /// reading code names rather than <see cref="Off"/>: a setting nobody has touched behaves the
    /// way the code that reads it says it behaves.
    /// </summary>
    public static class Toggles
    {
        /// <summary>Switched off.</summary>
        public const string Off = "off";

        /// <summary>Switched on.</summary>
        public const string On = "on";

        /// <summary>
        /// Both, in the order the settings form offers them. <see cref="On"/> is first because the
        /// form's dropdown has no blank entry and treats the first value as the default (D75), and
        /// every toggle so far is on unless somebody turns it off.
        /// </summary>
        public static IReadOnlyList<string> All { get; } = new[] { On, Off };

        /// <summary>
        /// Whether <paramref name="key"/> is switched on, with <paramref name="whenUnset"/> for a
        /// key nobody has stored or has cleared back to its default.
        /// </summary>
        public static bool Is(IReadOnlyDictionary<string, string> settings, string key, bool whenUnset)
        {
            if (!settings.TryGetValue(key, out var value))
                return whenUnset;

            var text = value.Trim();

            return text.Length == 0 ? whenUnset : IsOn(text);
        }

        /// <summary>Whether this is one of the two words a toggle may be stored as.</summary>
        public static bool IsKnown(string value) => All.Contains(value, StringComparer.Ordinal);

        /// <summary>Whether this value means on. Anything that is not <see cref="On"/> is off.</summary>
        public static bool IsOn(string value) => string.Equals(value.Trim(), On, StringComparison.Ordinal);
    }
}
