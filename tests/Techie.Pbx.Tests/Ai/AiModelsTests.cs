using System.Text;
using Techie.Pbx.Core.Ai;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Pages.Settings;

namespace Techie.Pbx.Tests.Ai
{
    /// <summary>
    /// The model chooser on the AI settings page (D207), without a network: OpenRouter's and
    /// Ollama's lists read, the shape a chosen model must have, and which row it is saved to.
    /// </summary>
    public class AiModelsTests
    {
        private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

        [Fact]
        public void OpenRouter_models_without_tools_are_left_out_and_prices_are_per_million()
        {
            var models = AiProviders.ReadOpenRouterModels(Bytes(
                """
                {"data":[
                  {"id":"openai/gpt-4o-mini","name":"OpenAI: GPT-4o-mini","context_length":128000,
                   "pricing":{"prompt":"0.00000015","completion":"0.0000006"},
                   "supported_parameters":["temperature","tools","tool_choice"]},
                  {"id":"some/no-tools","name":"No tools","context_length":8192,
                   "pricing":{"prompt":"0","completion":"0"},
                   "supported_parameters":["temperature"]}
                ]}
                """));

            var model = Assert.Single(models);
            Assert.Equal("openai/gpt-4o-mini", model.Id);
            Assert.Equal("OpenAI: GPT-4o-mini", model.Name);
            Assert.Equal(128000, model.ContextLength);
            Assert.Equal(0.15m, model.InputPricePerMillion);
            Assert.Equal(0.6m, model.OutputPricePerMillion);
            Assert.True(model.SupportsTools);
        }

        [Fact]
        public void OpenRouter_models_are_ordered_by_output_price_then_id()
        {
            var models = AiProviders.ReadOpenRouterModels(Bytes(
                """
                {"data":[
                  {"id":"b/dear","pricing":{"prompt":"0","completion":"0.00001"},"supported_parameters":["tools"]},
                  {"id":"z/cheap","pricing":{"prompt":"0.000001","completion":"0.000002"},"supported_parameters":["tools"]},
                  {"id":"a/cheap","pricing":{"prompt":"0.000001","completion":"0.000002"},"supported_parameters":["tools"]},
                  {"id":"openrouter/auto","pricing":{"prompt":"-1","completion":"-1"},"supported_parameters":["tools"]}
                ]}
                """));

            Assert.Equal(new[] { "a/cheap", "z/cheap", "b/dear" }, models.Select(m => m.Id));
        }

        /// <summary>
        /// Batch variants answer asynchronously on a cheaper tier: listing them would offer a model
        /// that cannot answer a live call in time (D207).
        /// </summary>
        [Fact]
        public void Batch_variants_are_not_offered()
        {
            var models = AiProviders.ReadOpenRouterModels(Bytes(
                """
                {"data":[
                  {"id":"anthropic/claude-haiku-5.5:batch","pricing":{"prompt":"0.00000005","completion":"0.00000025"},"supported_parameters":["tools"]},
                  {"id":"anthropic/claude-haiku-5.5","pricing":{"prompt":"0.0000001","completion":"0.0000005"},"supported_parameters":["tools"]}
                ]}
                """));

            var model = Assert.Single(models);
            Assert.Equal("anthropic/claude-haiku-5.5", model.Id);
        }

        [Fact]
        public void An_openrouter_body_without_data_is_a_provider_error()
        {
            var error = Assert.Throws<AiProviderException>(() => AiProviders.ReadOpenRouterModels(Bytes("""{"error":"nope"}""")));

            Assert.Contains("OpenRouter models", error.Message);
        }

        [Fact]
        public void Ollama_models_are_read_by_name()
        {
            var models = AiProviders.ReadOllamaModels(Bytes(
                """{"models":[{"name":"qwen2.5:7b","size":1},{"name":"llama3.2:latest","size":2}]}"""));

            Assert.Equal(new[] { "llama3.2:latest", "qwen2.5:7b" }, models.Select(m => m.Id));
            Assert.All(models, m => Assert.True(m.SupportsTools));
            Assert.All(models, m => Assert.Equal(0m, m.OutputPricePerMillion));
        }

        [Fact]
        public void No_Ollama_models_is_an_empty_list()
        {
            Assert.Empty(AiProviders.ReadOllamaModels(Bytes("""{"models":[]}""")));
        }

        [Theory]
        [InlineData("anthropic/claude-haiku-5.5")]
        [InlineData("llama3.2:latest")]
        public void A_model_slug_is_accepted(string model)
        {
            Assert.True(AiModel.IsPlausibleId(model));
        }

        [Fact]
        public void A_model_that_is_not_a_slug_is_refused()
        {
            Assert.False(AiModel.IsPlausibleId("<script>"));
            Assert.False(AiModel.IsPlausibleId(""));
            Assert.False(AiModel.IsPlausibleId(new string('a', 300)));
        }

        [Fact]
        public void The_chosen_model_is_saved_to_the_chosen_providers_row()
        {
            var ollama = AiSettingsSnapshot.From(new Dictionary<string, string> { [SettingsKeys.AiProvider] = AiModelProviders.Ollama });
            var openRouter = AiSettingsSnapshot.From(new Dictionary<string, string> { [SettingsKeys.AiProvider] = AiModelProviders.OpenRouter });

            Assert.Equal(SettingsKeys.AiOllamaModel, AiSettingsModel.ModelKeyFor(ollama));
            Assert.Equal(SettingsKeys.AiOpenRouterModel, AiSettingsModel.ModelKeyFor(openRouter));
        }

        [Fact]
        public void The_chooser_sits_above_each_providers_model_row()
        {
            var openRouter = AiSettingsModel.Sections(new Dictionary<string, string>()).Single(s => s.Title == AiModelProviders.Display(AiModelProviders.OpenRouter));
            var ollama = AiSettingsModel.Sections(new Dictionary<string, string> { [SettingsKeys.AiProvider] = AiModelProviders.Ollama })
                .Single(s => s.Title == AiModelProviders.Display(AiModelProviders.Ollama));

            Assert.Equal(SettingsKeys.AiOpenRouterModel, openRouter.InsertBeforeKey);
            Assert.Equal(SettingsKeys.AiOllamaModel, ollama.InsertBeforeKey);
        }

        [Fact]
        public void An_option_shows_the_id_and_its_price_per_million()
        {
            var model = new AiModel { Id = "openai/gpt-4o-mini", InputPricePerMillion = 0.15m, OutputPricePerMillion = 0.6m };

            Assert.Equal("openai/gpt-4o-mini — in $0.15 / out $0.60 per M", new ModelChoices().Label(model));
            Assert.Equal("openai/gpt-4o-mini", new ModelChoices { Provider = AiModelProviders.Ollama }.Label(model));
        }
    }
}
