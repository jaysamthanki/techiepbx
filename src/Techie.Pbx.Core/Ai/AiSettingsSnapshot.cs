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

        /// <summary>Whether both provider keys are present: without either, no call can be answered.</summary>
        public bool IsConfigured => this.ElevenLabsApiKey.Length > 0 && this.OpenRouterApiKey.Length > 0;

        public int MediaPort { get; init; } = AiSettings.DefaultMediaPort;

        public string OpenRouterApiKey { get; init; } = "";

        public string OpenRouterModel { get; init; } = "";

        public string SttModel { get; init; } = AiSettings.DefaultSttModel;

        public static AiSettingsSnapshot From(IReadOnlyDictionary<string, string> settings) => new()
        {
            BargeIn = Toggles.Is(settings, SettingsKeys.AiBargeIn, whenUnset: true),
            ElevenLabsApiKey = Text(settings, SettingsKeys.AiElevenLabsApiKey, ""),
            ElevenLabsModel = Text(settings, SettingsKeys.AiElevenLabsModel, AiSettings.DefaultTtsModel),
            ElevenLabsVoiceId = Text(settings, SettingsKeys.AiElevenLabsVoiceId, ""),
            MediaPort = Port(settings),
            OpenRouterApiKey = Text(settings, SettingsKeys.AiOpenRouterApiKey, ""),
            OpenRouterModel = Text(settings, SettingsKeys.AiOpenRouterModel, ""),
            SttModel = Text(settings, SettingsKeys.AiSttModel, AiSettings.DefaultSttModel),
        };

        /// <summary>
        /// Everything but the two keys. A record prints every property by default, and a snapshot
        /// that found its way into a log line must not take the customer's keys with it.
        /// </summary>
        public override string ToString() =>
            $"AiSettingsSnapshot {{ BargeIn = {this.BargeIn}, ElevenLabsModel = {this.ElevenLabsModel}, " +
            $"ElevenLabsVoiceId = {this.ElevenLabsVoiceId}, MediaPort = {this.MediaPort}, " +
            $"OpenRouterModel = {this.OpenRouterModel}, SttModel = {this.SttModel}, IsConfigured = {this.IsConfigured} }}";

        /// <summary>The same range the Settings page accepts; outside it, the default (D199).</summary>
        private static int Port(IReadOnlyDictionary<string, string> settings) =>
            int.TryParse(Text(settings, SettingsKeys.AiMediaPort, ""), out var port) && port is >= AiSettings.MinMediaPort and <= 65535
                ? port
                : AiSettings.DefaultMediaPort;

        private static string Text(IReadOnlyDictionary<string, string> settings, string key, string fallback)
        {
            if (!settings.TryGetValue(key, out var value))
                return fallback;

            var text = value.Trim();

            return text.Length == 0 ? fallback : text;
        }
    }
}
