namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// One extension's SIP password, sent only when an admin explicitly asks for it. Never part
    /// of a list response: the table must not carry every secret to the browser.
    /// </summary>
    public class SecretResponse
    {
        public string Number { get; set; } = "";
        public string Secret { get; set; } = "";
    }
}
