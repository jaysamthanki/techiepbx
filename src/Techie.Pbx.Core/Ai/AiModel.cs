using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// One model the chosen model provider offers, as the AI settings page lists it (D207). The
    /// name is the provider's own text, so a page renders it as text, never as HTML.
    /// </summary>
    public sealed partial record AiModel
    {
        /// <summary>The longest context the model takes, in tokens; 0 when the provider does not say.</summary>
        public int ContextLength { get; init; }

        /// <summary>The slug the setting stores: <c>vendor/model</c> on OpenRouter, the bare name on Ollama.</summary>
        public string Id { get; init; } = "";

        /// <summary>Dollars per million prompt tokens; 0 for a free model and for Ollama.</summary>
        public decimal InputPricePerMillion { get; init; }

        public string Name { get; init; } = "";

        /// <summary>Dollars per million completion tokens; 0 for a free model and for Ollama.</summary>
        public decimal OutputPricePerMillion { get; init; }

        /// <summary>Whether the model takes tools, without which it cannot call <c>transfer_call</c>.</summary>
        public bool SupportsTools { get; init; }

        /// <summary>
        /// Whether a chosen model is shaped like a slug the model rows accept: a letter or digit,
        /// then letters, digits and <c>. _ : / -</c>, up to 128 in all — the same rule
        /// <see cref="Data.SettingsValidation"/> applies to <c>Ai.OpenRouterModel</c> and
        /// <c>Ai.OllamaModel</c>, checked first so a refusal can say to load the list again.
        /// </summary>
        public static bool IsPlausibleId(string value) => IdPattern().IsMatch(value);

        [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:\-/]{0,127}\z")]
        private static partial Regex IdPattern();
    }
}
