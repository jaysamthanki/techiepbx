using System.Globalization;
using Techie.Pbx.Core.Ai;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// What the AI page's "Load models" button shows (D207): the tool-capable models the chosen
    /// model provider offers, or why there are none. Names are the provider's text and are rendered
    /// as text.
    /// </summary>
    public class ModelChoices
    {
        /// <summary>The model stored now, selected in the list when it is one of them.</summary>
        public string Current { get; set; } = "";

        /// <summary>Why the list could not be loaded, as a sentence; blank when it was.</summary>
        public string Error { get; set; } = "";

        public List<AiModel> Models { get; set; } = new();

        /// <summary>The model provider the list is from: <see cref="AiModelProviders.OpenRouter"/> or <see cref="AiModelProviders.Ollama"/>.</summary>
        public string Provider { get; set; } = AiModelProviders.Default;

        /// <summary>
        /// The models in the select's groups: by vendor, the part of an OpenRouter id before its
        /// <c>/</c>, groups in name order and cheapest first within each; Ollama's are one group.
        /// </summary>
        public IEnumerable<IGrouping<string, AiModel>> Groups() =>
            this.Provider == AiModelProviders.Ollama
                ? this.Models.GroupBy(_ => AiModelProviders.Display(AiModelProviders.Ollama))
                : this.Models.GroupBy(m => m.Id.Contains('/') ? m.Id[..m.Id.IndexOf('/')] : "other").OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// An option's text: the id and, on OpenRouter, its price per million tokens each way, e.g.
        /// <c>openai/gpt-4o-mini — in $0.15 / out $0.60 per M</c>. Ollama's is the bare name.
        /// </summary>
        public string Label(AiModel model) =>
            this.Provider == AiModelProviders.Ollama
                ? model.Id
                : $"{model.Id} — in ${Price(model.InputPricePerMillion)} / out ${Price(model.OutputPricePerMillion)} per M";

        private static string Price(decimal perMillion) => perMillion.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
