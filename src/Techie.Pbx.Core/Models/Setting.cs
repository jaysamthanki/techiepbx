namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// One row of the Settings key/value table. Callers normally work with the plain strings
    /// <see cref="Data.SettingsRepository"/> hands back rather than with this type.
    /// </summary>
    public class Setting
    {
        public long SettingID { get; set; }
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
