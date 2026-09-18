namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// One line of a settings table, on the general page and the SIP page alike. Deliberately not
    /// the stored value for a secret: a table must never carry a credential to the browser, so
    /// <see cref="Value"/> is already masked by the time it gets here (D68).
    /// </summary>
    public class SettingRow
    {
        /// <summary>What a stored secret looks like in a table.</summary>
        public const string Mask = "••••••••";

        /// <summary>What the code uses when nothing is stored, or blank when there is no default.</summary>
        public string Default { get; set; } = "";

        public string Description { get; set; } = "";

        /// <summary>Whether a value is stored, which for a secret is all the table may say.</summary>
        public bool IsSet { get; set; }

        public string Key { get; set; } = "";

        /// <summary>The stored value as the table shows it: masked for a secret, blank for unset.</summary>
        public string Value { get; set; } = "";

        /// <summary>
        /// One row for one key, given everything that is stored. The one place a stored value is
        /// turned into something a page may render, so no page has to remember to mask a secret.
        /// </summary>
        public static SettingRow For(SettingDescriptor descriptor, IReadOnlyDictionary<string, string> stored)
        {
            var isSet = stored.TryGetValue(descriptor.Key, out var value) && value.Length > 0;

            return new SettingRow
            {
                Default = descriptor.Default,
                Description = descriptor.Description,
                IsSet = isSet,
                Key = descriptor.Key,
                Value = !isSet ? "" : descriptor.IsSecret ? Mask : value!,
            };
        }
    }
}
