using Techie.Pbx.Asterisk.Config;

namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// Which channel is the AI agent's call. The dialplan gives every call its own UUID
    /// (<c>Set(AI_CALL=${UUID()})</c>, D208) and passes it to <c>AudioSocket()</c>, which reports it
    /// as the first part of its application data; the socket carries the same UUID, so the media
    /// service binds the socket to its channel by it. A per-call UUID cannot match two channels, so
    /// the ambiguity rule D203 needed is gone. Pure, so the rule can be tested without AMI.
    /// </summary>
    public static class AiAgentChannel
    {
        /// <summary>The application name Asterisk reports for a channel inside <c>AudioSocket()</c>.</summary>
        public const string AudioSocketApplication = "AudioSocket";

        /// <summary>
        /// The channel inside <c>AudioSocket()</c> whose data starts with <paramref name="uuid"/>
        /// (<c>&lt;uuid&gt;,127.0.0.1:&lt;port&gt;</c>), or null when none does — the caller has
        /// gone, or the socket is not one of ours. Its <see cref="ActiveChannel.Exten"/> is the
        /// agent's number.
        /// </summary>
        public static ActiveChannel? Bind(IReadOnlyList<ActiveChannel> channels, string uuid)
        {
            if (uuid.Length == 0)
                return null;

            return channels.FirstOrDefault(c =>
                string.Equals(c.Application, AudioSocketApplication, StringComparison.OrdinalIgnoreCase) &&
                c.ApplicationData.StartsWith(uuid + ",", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The fallback for a session that was given no channel (D203's rule, kept only for that):
        /// the one channel in <c>AudioSocket()</c> at the agent's number in the internal context, or
        /// null with <paramref name="matches"/> saying why — none, or more than one, which cannot be
        /// told apart without the call's UUID.
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
