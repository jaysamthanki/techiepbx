namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// Where an AI agent's chat half runs: the values <see cref="Data.SettingsKeys.AiProvider"/> may
    /// name (D204). Only the model moves; speech stays ElevenLabs whichever is chosen. A list in code
    /// rather than a free-form name because each one is a request and a response shape this
    /// application has to speak, and a third is a code change.
    /// </summary>
    public static class AiModelProviders
    {
        /// <summary>What a stored blank means: OpenRouter (D204).</summary>
        public const string Default = OpenRouter;

        /// <summary>A server that speaks Ollama's <c>/api/chat</c>, on the customer's own network.</summary>
        public const string Ollama = "ollama";

        /// <summary>OpenRouter, on the customer's own key. The default (D204).</summary>
        public const string OpenRouter = "openrouter";

        /// <summary>Both, in the order the settings form offers them: the default first (D75).</summary>
        public static IReadOnlyList<string> All { get; } = new[] { OpenRouter, Ollama };

        /// <summary>The name a person reads, for a page heading or a log line.</summary>
        public static string Display(string value) => value switch
        {
            Ollama => "Ollama (compatible)",
            OpenRouter => "OpenRouter",
            _ => value,
        };

        /// <summary>Whether this is a provider this application knows how to talk to.</summary>
        public static bool IsKnown(string value) => All.Contains(value, StringComparer.Ordinal);
    }
}
