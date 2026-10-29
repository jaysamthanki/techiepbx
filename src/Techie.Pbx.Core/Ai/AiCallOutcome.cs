namespace Techie.Pbx.Core.Ai
{
    /// <summary>How an AI agent's call ended, for the one log line each call gets (D200).</summary>
    public enum AiCallOutcome
    {
        /// <summary>The caller hung up, or Asterisk closed the socket.</summary>
        CallerHungUp,

        /// <summary>Something other than a provider failed; the socket was closed and the dialplan carried on.</summary>
        Failed,

        /// <summary>A provider failed; the socket was closed so the dialplan hands off or hangs up.</summary>
        ProviderFailed,

        /// <summary>Three turns in a row with nothing said; the socket was closed.</summary>
        Silent,

        /// <summary>The app is stopping.</summary>
        Stopped,

        /// <summary>The 10-minute cap was reached.</summary>
        TimedOut,

        /// <summary>The model asked for a person; the socket was closed so the dialplan transfers.</summary>
        Transferred,
    }
}
