using System.Diagnostics;
using System.Net;
using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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

        public void OnGet()
        {
        }

        /// <summary>The groups, each a small table of rows.</summary>
        public PartialViewResult OnGetTable() => this.Partial("_Sections", Sections(this.settings.GetAll()));

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
                    Rows = Rows(stored, SettingsKeys.AiOllamaUrl, SettingsKeys.AiOllamaModel),
                    Title = AiModelProviders.Display(AiModelProviders.Ollama),
                }
                : new SettingSection
                {
                    Help = "Your own OpenRouter account: the key it bills, and the model an agent asks for when it names none.",
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
