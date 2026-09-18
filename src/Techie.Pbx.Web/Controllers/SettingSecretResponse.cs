namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// One secret setting's stored value, sent only when an admin explicitly asks for it. Never
    /// part of the settings table: that page must not carry a credential to the browser (D68).
    /// </summary>
    public class SettingSecretResponse
    {
        public string Key { get; set; } = "";
        public string Secret { get; set; } = "";
    }
}
