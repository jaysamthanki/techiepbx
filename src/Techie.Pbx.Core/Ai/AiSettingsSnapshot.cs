using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// The <c>Ai.*</c> settings (D198) as one call reads them, taken once when the call arrives so
    /// a save on the Settings page mid-call cannot change the call's voice or key under it. A
    /// blank or unreadable value is the <see cref="AiSettings"/> default, never an error: the
    /// Settings page is what reports a bad value.
    /// </summary>
    public sealed record AiSettingsSnapshot
    {
        public bool BargeIn { get; init; } = true;

        public string ElevenLabsApiKey { get; init; } = "";

        public string ElevenLabsModel { get; init; } = AiSettings.DefaultTtsModel;

        public string ElevenLabsVoiceId { get; init; } = "";

        /// <summary>
        /// Whether a call could be answered with no agent override: speech is set up and so is the
        /// chosen model provider (D204). <see cref="Missing"/> says what is not.
        /// </summary>
        public bool IsConfigured => this.Missing("").Length == 0;

        /// <summary>Whether the log carries the words of each turn (F11, D200).</summary>
        public bool LogTranscripts { get; init; } = true;

        public int MediaPort { get; init; } = AiSettings.DefaultMediaPort;

        /// <summary>The model the Ollama server is asked for when the agent names none (D204).</summary>
        public string OllamaModel { get; init; } = "";

        /// <summary>The Ollama server's base URL, without a trailing slash (D204).</summary>
        public string OllamaUrl { get; init; } = "";

        public string OpenRouterApiKey { get; init; } = "";

        public string OpenRouterModel { get; init; } = "";

        /// <summary>Which <see cref="AiModelProviders"/> value answers the chat half (D204).</summary>
        public string Provider { get; init; } = AiModelProviders.Default;

        public string SttModel { get; init; } = AiSettings.DefaultSttModel;

        public static AiSettingsSnapshot From(IReadOnlyDictionary<string, string> settings) => new()
        {
            BargeIn = Toggles.Is(settings, SettingsKeys.AiBargeIn, whenUnset: true),
            LogTranscripts = Toggles.Is(settings, SettingsKeys.AiLogTranscripts, whenUnset: true),
            ElevenLabsApiKey = Text(settings, SettingsKeys.AiElevenLabsApiKey, ""),
            ElevenLabsModel = Text(settings, SettingsKeys.AiElevenLabsModel, AiSettings.DefaultTtsModel),
            ElevenLabsVoiceId = Text(settings, SettingsKeys.AiElevenLabsVoiceId, ""),
            MediaPort = Port(settings),
            OllamaModel = Text(settings, SettingsKeys.AiOllamaModel, ""),
            OllamaUrl = Text(settings, SettingsKeys.AiOllamaUrl, "").TrimEnd('/'),
            OpenRouterApiKey = Text(settings, SettingsKeys.AiOpenRouterApiKey, ""),
            OpenRouterModel = Text(settings, SettingsKeys.AiOpenRouterModel, ""),
            Provider = ProviderOf(settings),
            SttModel = Text(settings, SettingsKeys.AiSttModel, AiSettings.DefaultSttModel),
        };

        /// <summary>
        /// What stops a call from being answered, as one or two sentences for a log line, or empty
        /// when nothing does: <see cref="MissingForChat"/> and <see cref="MissingForSpeech"/> together.
        /// </summary>
        public string Missing(string agentModel) =>
            string.Join(" ", new[] { this.MissingForChat(agentModel), this.MissingForSpeech() }.Where(m => m.Length > 0));

        /// <summary>
        /// What the chosen model provider still needs, as one sentence, or empty (D204). OpenRouter
        /// needs its key; Ollama its URL and a model, where <paramref name="agentModel"/>, the
        /// agent's own, stands in for a blank <see cref="OllamaModel"/>.
        /// </summary>
        public string MissingForChat(string agentModel)
        {
            var missing = new List<string>();

            if (this.Provider == AiModelProviders.Ollama)
            {
                if (this.OllamaUrl.Length == 0)
                    missing.Add("the Ollama URL");

                if (agentModel.Length == 0 && this.OllamaModel.Length == 0)
                    missing.Add("the Ollama model");
            }
            else if (this.OpenRouterApiKey.Length == 0)
            {
                missing.Add("the OpenRouter key");
            }

            return missing.Count == 0
                ? ""
                : $"{AiModelProviders.Display(this.Provider)} is the model provider, but {string.Join(" and ", missing)} {(missing.Count == 1 ? "is" : "are")} not set.";
        }

        /// <summary>What ElevenLabs, the one speech provider, still needs, as one sentence, or empty.</summary>
        public string MissingForSpeech() => this.ElevenLabsApiKey.Length == 0 ? "The ElevenLabs key is not set." : "";

        /// <summary>The model one call asks for: the agent's own, else the chosen provider's site default.</summary>
        public string ModelFor(string agentModel) =>
            agentModel.Length > 0 ? agentModel
            : this.Provider == AiModelProviders.Ollama ? this.OllamaModel
            : this.OpenRouterModel;

        /// <summary>
        /// Everything but the two keys. A record prints every property by default, and a snapshot
        /// that found its way into a log line must not take the customer's keys with it.
        /// </summary>
        public override string ToString() =>
            $"AiSettingsSnapshot {{ BargeIn = {this.BargeIn}, ElevenLabsModel = {this.ElevenLabsModel}, " +
            $"ElevenLabsVoiceId = {this.ElevenLabsVoiceId}, MediaPort = {this.MediaPort}, " +
            $"OllamaModel = {this.OllamaModel}, OllamaUrl = {this.OllamaUrl}, " +
            $"OpenRouterModel = {this.OpenRouterModel}, Provider = {this.Provider}, SttModel = {this.SttModel}, " +
            $"IsConfigured = {this.IsConfigured}, LogTranscripts = {this.LogTranscripts} }}";

        /// <summary>The same range the Settings page accepts; outside it, the default (D199).</summary>
        private static int Port(IReadOnlyDictionary<string, string> settings) =>
            int.TryParse(Text(settings, SettingsKeys.AiMediaPort, ""), out var port) && port is >= AiSettings.MinMediaPort and <= 65535
                ? port
                : AiSettings.DefaultMediaPort;

        /// <summary>A provider this application knows, else the default: the page reports a bad value, not the call.</summary>
        private static string ProviderOf(IReadOnlyDictionary<string, string> settings)
        {
            var provider = Text(settings, SettingsKeys.AiProvider, AiModelProviders.Default);

            return AiModelProviders.IsKnown(provider) ? provider : AiModelProviders.Default;
        }

        private static string Text(IReadOnlyDictionary<string, string> settings, string key, string fallback)
        {
            if (!settings.TryGetValue(key, out var value))
                return fallback;

            var text = value.Trim();

            return text.Length == 0 ? fallback : text;
        }
    }
}
