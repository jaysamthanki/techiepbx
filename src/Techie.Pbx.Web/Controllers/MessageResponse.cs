namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// An error the page can show as it is. Only messages we wrote ourselves go in here, never
    /// an exception's stack or anything about the server's insides.
    /// </summary>
    public class MessageResponse
    {
        public string Message { get; set; } = "";

        public MessageResponse(string message)
        {
            this.Message = message;
        }
    }
}
