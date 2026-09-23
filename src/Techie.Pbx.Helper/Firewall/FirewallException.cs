namespace Techie.Pbx.Helper.Firewall
{
    /// <summary>
    /// A firewall command that did not work, carrying the sentence the admin should see. It is a
    /// type of its own so the dispatcher can tell "nft refused this ruleset", which an admin can
    /// act on and should read, from an unexpected exception, whose message is about our code and
    /// goes to the journal instead.
    /// </summary>
    public class FirewallException : Exception
    {
        public FirewallException(string message) : base(message)
        {
        }
    }
}
