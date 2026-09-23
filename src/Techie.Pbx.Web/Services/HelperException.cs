namespace Techie.Pbx.Web.Services
{
    /// <summary>
    /// The helper could not be reached, or refused what it was asked. The message is either the
    /// Helper's own sentence — nft's words about a ruleset it would not load, say — or a sentence
    /// about the socket itself, and either way it is written to be shown to an admin as it is.
    /// Nothing here is caught and turned into a blank page (D142).
    /// </summary>
    public class HelperException : Exception
    {
        public HelperException(string message) : base(message)
        {
        }
    }
}
