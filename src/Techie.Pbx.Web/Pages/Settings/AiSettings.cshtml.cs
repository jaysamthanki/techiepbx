using System.Diagnostics;
using System.Net;
using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Ai;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// The AI settings page (F11, D204): the <c>Ai.*</c> keys grouped into the model provider,
    /// speech and conversation, and two buttons that try the providers for real.
    ///
    /// A display shell like the SIP page: rows open the general settings page's edit form in the
    /// shared modal and that page saves them, so a setting is validated, written and logged in one
    /// place. Only the chosen provider's group is shown, decided here from what is stored; saving
    /// the provider answers "settingsChanged" and the groups come back for the new one, so there is
    /// no client code to keep in step.
    ///
    /// The Speech group also offers the voices the saved ElevenLabs key may use (D205), loaded only
    /// when asked for. Choosing one stores the same <c>Ai.ElevenLabsVoiceId</c> row through the same
    /// repository; the row stays the setting and can still be typed.
    ///
    /// The provider's group offers the provider's own model list the same way (D207): tool-capable
    /// models only, stored as that provider's model row.
    /// </summary>
    public class AiSettingsModel : PageModel
    {
        /// <summary>The sentence the model test asks for.</summary>
        public const string TestPrompt = "Reply with the single word OK";

        /// <summary>The sentence the voice test speaks.</summary>
        public const string TestSentence = "This is a test of the Techie PBX voice.";

        /// <summary>How long either test waits for its provider.</summary>
        public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// The client the tests use. No client-wide timeout: each request carries its own, as the
        /// media listener's does (<see cref="AiProviders"/>).
        /// </summary>
        private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

        private static readonly ILog Log = LogManager.GetLogger(typeof(AiSettingsModel));

        private readonly SettingsRepository settings;

        public AiSettingsModel()
        {
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        /// <summary>
        /// The row a chosen model is stored in: <c>Ai.OllamaModel</c> when the provider is Ollama,
        /// <c>Ai.OpenRouterModel</c> otherwise. Pure, so it can be checked without hosting the page.
        /// </summary>
        public static string ModelKeyFor(AiSettingsSnapshot settings) =>
            settings.Provider == AiModelProviders.Ollama ? SettingsKeys.AiOllamaModel : SettingsKeys.AiOpenRouterModel;

        public void OnGet()
        {
        }

        /// <summary>
        /// The tool-capable models the chosen provider offers, as a list to choose from, or the
        /// sentence that says why not (D207). Never an error page. Served from
        /// <see cref="AiListCache{T}"/> where it can be, keyed by the provider and what decides its
        /// list — the OpenRouter key, or the Ollama URL — so a changed one is a fresh list.
        /// </summary>
        public async Task<PartialViewResult> OnGetModelsAsync()
        {
            var settings = AiSettingsSnapshot.From(this.settings.GetAll());
            var ollama = settings.Provider == AiModelProviders.Ollama;
            var choices = new ModelChoices
            {
                Current = ollama ? settings.OllamaModel : settings.OpenRouterModel,
                Provider = settings.Provider,
            };

            try
            {
                // Not the request's token, for the same reason as the voices: the load is shared.
                choices.Models = await AiListCache<AiModel>.GetOrAdd(
                    ollama ? $"{AiModelProviders.Ollama} {settings.OllamaUrl}" : $"{AiModelProviders.OpenRouter} {settings.OpenRouterApiKey}",
                    DateTimeOffset.UtcNow,
                    () => AiProviders.ListModelsAsync(Http, settings, CancellationToken.None));
            }
            catch (AiProviderException ex)
            {
                Log.Warn($"AI model list for {this.User.Identity?.Name}: {ex.Message}");
                choices.Error = ex.Message;
            }
            catch (OperationCanceledException)
            {
                choices.Error = $"{AiModelProviders.Display(settings.Provider)} did not answer within {AiProviders.ModelsTimeout.TotalSeconds:0} seconds.";
            }

            return this.Partial("_ModelChoices", choices);
        }

        /// <summary>The groups, each a small table of rows.</summary>
        public PartialViewResult OnGetTable() => this.Partial("_Sections", Sections(this.settings.GetAll()));

        /// <summary>
        /// The voices the saved ElevenLabs key may use, as a list to choose from, or the sentence
        /// that says why not (D205). Never an error page: a refusal is the admin's to act on. Served
        /// from <see cref="AiListCache{T}"/> where it can be, so pressing the button again is free.
        /// </summary>
        public async Task<PartialViewResult> OnGetVoicesAsync()
        {
            var settings = AiSettingsSnapshot.From(this.settings.GetAll());
            var choices = new VoiceChoices { Current = settings.ElevenLabsVoiceId };

            if (settings.ElevenLabsApiKey.Length == 0)
            {
                choices.Error = "Set the ElevenLabs key first.";
                return this.Partial("_VoiceChoices", choices);
            }

            try
            {
                // Not the request's token: the load is shared through the cache, and an admin
                // navigating away must not cancel it for whoever else is waiting on it. The
                // provider call has its own timeout.
                choices.Voices = await AiListCache<AiVoice>.GetOrAdd(
                    settings.ElevenLabsApiKey,
                    DateTimeOffset.UtcNow,
                    () => AiProviders.ListVoicesAsync(Http, settings, CancellationToken.None));
            }
            catch (AiProviderException ex)
            {
                Log.Warn($"AI voice list for {this.User.Identity?.Name}: {ex.Message}");
                choices.Error = ex.Message;
            }
            catch (OperationCanceledException)
            {
                choices.Error = $"ElevenLabs did not answer within {AiProviders.VoicesTimeout.TotalSeconds:0} seconds.";
            }

            return this.Partial("_VoiceChoices", choices);
        }

        /// <summary>
        /// Stores the chosen model as the chosen provider's model row (<see cref="ModelKeyFor"/>),
        /// through the repository the general settings page saves with. Only a slug shaped like one
        /// the row accepts (<see cref="AiModel.IsPlausibleId"/>) is taken; anything else is refused
        /// with nothing written.
        /// </summary>
        public IActionResult OnPostSaveModel(string? model)
        {
            var id = (model ?? "").Trim();

            if (!AiModel.IsPlausibleId(id))
                return this.Alert("That is not a model the provider listed. Load the models again and choose one from the list.");

            var key = ModelKeyFor(AiSettingsSnapshot.From(this.settings.GetAll()));

            try
            {
                this.settings.Set(key, id);
            }
            catch (ValidationFailedException ex)
            {
                return this.Alert(string.Join(" ", ex.Errors));
            }

            Log.Info($"Setting {key} chosen from the model list by {this.User.Identity?.Name}");
            return this.Saved(key);
        }

        /// <summary>
        /// Stores the chosen voice as <c>Ai.ElevenLabsVoiceId</c>, through the repository the general
        /// settings page saves with, so validation and logging are the same. Only an ID shaped like
        /// one ElevenLabs issues is accepted (<see cref="AiVoice.IsPlausibleId"/>); anything else is
        /// refused with nothing written.
        /// </summary>
        public IActionResult OnPostSaveVoice(string? voiceId)
        {
            var id = (voiceId ?? "").Trim();

            if (!AiVoice.IsPlausibleId(id))
                return this.Alert("That is not an ElevenLabs voice ID. Load the voices again and choose one from the list.");

            try
            {
                this.settings.Set(SettingsKeys.AiElevenLabsVoiceId, id);
            }
            catch (ValidationFailedException ex)
            {
                return this.Alert(string.Join(" ", ex.Errors));
            }

            Log.Info($"Setting {SettingsKeys.AiElevenLabsVoiceId} chosen from the voice list by {this.User.Identity?.Name}");
            return this.Saved(SettingsKeys.AiElevenLabsVoiceId);
        }

        /// <summary>
        /// One tiny chat through the chosen model provider, with no tools, so an admin can tell a
        /// wrong URL, key or model from a working one without placing a call.
        /// </summary>
        public async Task<IActionResult> OnPostTestModelAsync(CancellationToken cancellationToken)
        {
            var settings = AiSettingsSnapshot.From(this.settings.GetAll());
            var provider = AiModelProviders.Display(settings.Provider);
            var missing = settings.MissingForChat("");

            if (missing.Length > 0)
                return this.Alert(missing);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TestTimeout);

            try
            {
                var clock = Stopwatch.StartNew();
                var reply = await AiProviders.ChatAsync(Http, settings, settings.ModelFor(""), new[] { ("user", TestPrompt) }, [], timeout.Token);

                Log.Info($"AI model test by {this.User.Identity?.Name}: {provider} answered in {clock.ElapsedMilliseconds} ms");
                return this.Toast($"{provider} answered in {clock.ElapsedMilliseconds} ms: {Shorten(reply.Content)}");
            }
            catch (AiProviderException ex)
            {
                Log.Warn($"AI model test by {this.User.Identity?.Name}: {ex.Message}");
                return this.Alert(ex.Message);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return this.Alert($"{provider} did not answer within {TestTimeout.TotalSeconds:0} seconds.");
            }
        }

        /// <summary>One sentence of ElevenLabs speech, reported as its length rather than played.</summary>
        public async Task<IActionResult> OnPostTestVoiceAsync(CancellationToken cancellationToken)
        {
            var settings = AiSettingsSnapshot.From(this.settings.GetAll());
            var missing = settings.MissingForSpeech();

            if (missing.Length > 0)
                return this.Alert(missing);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TestTimeout);

            try
            {
                var audio = await AiProviders.SpeakAsync(Http, settings, settings.ElevenLabsModel, settings.ElevenLabsVoiceId, TestSentence, timeout.Token);

                // ulaw_8000 is one byte per sample at 8 kHz: eight bytes to the millisecond.
                var milliseconds = audio.Length / 8;

                Log.Info($"AI voice test by {this.User.Identity?.Name}: {milliseconds} ms of audio");
                return this.Toast($"ElevenLabs spoke the test sentence: {milliseconds} ms of audio.");
            }
            catch (AiProviderException ex)
            {
                Log.Warn($"AI voice test by {this.User.Identity?.Name}: {ex.Message}");
                return this.Alert(ex.Message);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return this.Alert($"ElevenLabs did not answer within {TestTimeout.TotalSeconds:0} seconds.");
            }
        }

        /// <summary>
        /// The groups this page shows for what is stored: the provider choice, the chosen provider's
        /// own settings, speech, and the conversation. Pure, so which keys appear where can be
        /// checked without a database. A future speech provider is one more choice and one more
        /// group here, the way the model provider's two are.
        /// </summary>
        public static List<SettingSection> Sections(IReadOnlyDictionary<string, string> stored)
        {
            var provider = AiSettingsSnapshot.From(stored).Provider;

            var providerSection = provider == AiModelProviders.Ollama
                ? new SettingSection
                {
                    Help = "An Ollama-compatible server on your own network. No API key: the server is yours, " +
                        "so only its address and the model to ask for are needed.",
                    InsertBeforeKey = SettingsKeys.AiOllamaModel,
                    InsertPartial = "/Pages/Settings/_ModelChooser.cshtml",
                    Rows = Rows(stored, SettingsKeys.AiOllamaUrl, SettingsKeys.AiOllamaModel),
                    Title = AiModelProviders.Display(AiModelProviders.Ollama),
                }
                : new SettingSection
                {
                    Help = "Your own OpenRouter account: the key it bills, and the model an agent asks for when it names none.",
                    InsertBeforeKey = SettingsKeys.AiOpenRouterModel,
                    InsertPartial = "/Pages/Settings/_ModelChooser.cshtml",
                    Rows = Rows(stored, SettingsKeys.AiOpenRouterApiKey, SettingsKeys.AiOpenRouterModel),
                    Title = AiModelProviders.Display(AiModelProviders.OpenRouter),
                };

            return new List<SettingSection>
            {
                new()
                {
                    Help = "Which service writes the agents' replies. Changing it changes only that: speech stays " +
                        "ElevenLabs. The settings for the provider chosen here are shown below it.",
                    Rows = Rows(stored, SettingsKeys.AiProvider),
                    Title = "Model provider",
                },
                providerSection,
                new()
                {
                    Help = "ElevenLabs hears the caller and speaks the replies, on your own key.",
                    InsertBeforeKey = SettingsKeys.AiElevenLabsVoiceId,
                    InsertPartial = "/Pages/Settings/_VoiceChooser.cshtml",
                    Rows = Rows(stored, SettingsKeys.AiElevenLabsApiKey, SettingsKeys.AiElevenLabsVoiceId, SettingsKeys.AiElevenLabsModel, SettingsKeys.AiSttModel),
                    Title = "Speech",
                },
                new()
                {
                    Help = "How a call behaves and what is written down about it. The media port is written into " +
                        "the dialplan, so a change to it needs an apply and an app restart.",
                    Rows = Rows(stored, SettingsKeys.AiBargeIn, SettingsKeys.AiLogTranscripts, SettingsKeys.AiMediaPort),
                    Title = "Conversation",
                },
            };
        }

        /// <summary>A failure, shown as site.js's alert, which renders it as text.</summary>
        private IActionResult Alert(string message) => this.Trigger("pbxAlert", message);

        private static List<SettingRow> Rows(IReadOnlyDictionary<string, string> stored, params string[] keys) =>
            keys.Select(key => SettingRow.For(SettingsCatalog.For(key), stored)).ToList();

        /// <summary>
        /// A setting chosen from a list, answered with the same events the general page's save
        /// answers with: the groups reload, so the row shows the new value. App-scoped, so there is
        /// nothing to apply.
        /// </summary>
        private IActionResult Saved(string key)
        {
            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["settingsChanged"] = null,
                ["pbxToast"] = new { message = WebUtility.HtmlEncode($"{key} saved.") },
            });
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>A model's reply, flattened and cut short enough for a toast.</summary>
        private static string Shorten(string text)
        {
            var flat = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();

            return flat.Length == 0 ? "(an empty reply)" : flat.Length > 100 ? flat[..100] + "…" : flat;
        }

        /// <summary>
        /// A success, shown as site.js's toast. The toast renders its message as HTML and part of
        /// this one is a model's words, so it is encoded here.
        /// </summary>
        private IActionResult Toast(string message) => this.Trigger("pbxToast", WebUtility.HtmlEncode(message));

        /// <summary>No content, and one event carrying the message, the way every other change answers.</summary>
        private IActionResult Trigger(string name, string message)
        {
            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(new Dictionary<string, object> { [name] = new { message } });
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }
    }
}
