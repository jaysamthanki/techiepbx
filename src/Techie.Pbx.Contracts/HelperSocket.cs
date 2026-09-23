namespace Techie.Pbx.Contracts
{
    /// <summary>
    /// Where the Helper listens and the limits both ends hold each other to (D142). Constants
    /// rather than configuration: the socket path is compiled into the one client and the one
    /// server, so there is no setting anybody could point either of them somewhere else.
    /// </summary>
    public static class HelperSocket
    {
        /// <summary>The directory the socket lives in: 0750 root:tnpbx, so only the web user's group can reach it.</summary>
        public const string DirectoryPath = "/run/tnpbx";

        /// <summary>
        /// The most a request may be. A rule is well under 100 bytes and there are at most
        /// <see cref="MaxRules"/> of them, so this is roomy; what it is for is making sure a
        /// caller that never sends a newline cannot make the Helper read forever.
        /// </summary>
        public const int MaxRequestBytes = 65536;

        /// <summary>How many rules one firewall.apply may carry. More than any real system needs.</summary>
        public const int MaxRules = 32;

        /// <summary>The socket itself: 0660 root:tnpbx.</summary>
        public const string Path = "/run/tnpbx/helper.sock";

        /// <summary>What <c>ping</c> answers with, so a client can tell an old Helper from a new one.</summary>
        public const int ProtocolVersion = 1;

        /// <summary>
        /// How long either end waits. Everything the Helper does is local and quick — the slowest
        /// is two nft invocations — so a caller that is still waiting after this is a caller
        /// something has gone wrong for, and a page that hangs is worse than a page that says so.
        /// </summary>
        public const int TimeoutSeconds = 5;
    }
}
