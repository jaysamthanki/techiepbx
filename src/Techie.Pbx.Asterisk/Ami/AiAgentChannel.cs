using Techie.Pbx.Asterisk.Config;

namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// Which channel is the AI agent's call (D203). The media socket carries a UUID that names the
    /// agent, not the call, so the channel to redirect is found by where it is: in
    /// <c>AudioSocket()</c>, at the agent's number, in the internal context. Pure, so the rule can
    /// be tested without AMI.
    /// </summary>
    public static class AiAgentChannel
    {
        /// <summary>The application name Asterisk reports for a channel inside <c>AudioSocket()</c>.</summary>
        public const string AudioSocketApplication = "AudioSocket";

        /// <summary>
        /// The one channel in the agent's <c>AudioSocket()</c>, or null with
        /// <paramref name="matches"/> saying why: none (the caller has gone) or more than one. Two
        /// callers talking to the same agent at once are indistinguishable from here, and moving
        /// one caller's call on the other's request would be worse than not moving it at all.
        /// </summary>
        public static ActiveChannel? Find(IReadOnlyList<ActiveChannel> channels, string agentNumber, out int matches)
        {
            var found = channels
                .Where(c =>
                    string.Equals(c.Application, AudioSocketApplication, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.Context, ExtensionsConfRenderer.InternalContext, StringComparison.Ordinal) &&
                    string.Equals(c.Exten, agentNumber, StringComparison.Ordinal))
                .ToList();

            matches = found.Count;
            return found.Count == 1 ? found[0] : null;
        }
    }
}
