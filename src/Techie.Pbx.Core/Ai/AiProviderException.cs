namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// A speech or model provider failed (D200). The message is safe to log: a status code and the
    /// provider's own error text, with any key scrubbed out, and never a transcript or a reply.
    /// </summary>
    public class AiProviderException : Exception
    {
        public AiProviderException(string message)
            : base(message)
        {
        }
    }
}
