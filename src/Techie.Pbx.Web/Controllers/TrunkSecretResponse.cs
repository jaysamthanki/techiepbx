namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// One trunk's provider password, sent only when an admin explicitly asks for it. Never part
    /// of a list response: the table must not carry every password to the browser.
    /// </summary>
    public class TrunkSecretResponse
    {
        public string Name { get; set; } = "";
        public string Secret { get; set; } = "";
    }
}
