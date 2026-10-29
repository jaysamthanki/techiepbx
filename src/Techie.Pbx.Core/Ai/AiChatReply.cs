namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// What one chat completion answered (D203): words to speak, a tool call, or — tolerated, as
    /// a model sometimes does — neither, which is an empty reply.
    /// </summary>
    public sealed record AiChatReply
    {
        /// <summary>The words to speak; empty when the model only called a tool, or said nothing.</summary>
        public string Content { get; init; } = "";

        /// <summary>The first tool call the model made, or null for none.</summary>
        public AiToolCall? ToolCall { get; init; }

        /// <summary>A plain spoken reply, with no tool call.</summary>
        public static AiChatReply Text(string content) => new() { Content = content };
    }
}
