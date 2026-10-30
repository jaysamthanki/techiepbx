using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// The three provider calls one turn makes (D200): ElevenLabs speech-to-text, a chat completion
    /// from the chosen model provider — OpenRouter, or the customer's own Ollama server (D204) —
    /// and ElevenLabs text-to-speech in <c>ulaw_8000</c>. Plain request/response
    /// over the one shared <see cref="HttpClient"/>, each with its own timeout, because a caller
    /// on the line cannot wait for a provider that has stopped answering.
    ///
    /// The settings page also lists the voices the saved ElevenLabs key may use (D205) and the
    /// models the chosen model provider offers (D207).
    ///
    /// The chat can offer the model one tool, <see cref="TransferToolName"/>, whose only argument
    /// is one of the extensions it was given (D203); with none given, no tool is offered at all.
    ///
    /// Every failure — a status code, a timeout, a network error, an answer that is not what was
    /// asked for — is an <see cref="AiProviderException"/> whose message names no key.
    /// </summary>
    public static class AiProviders
    {
        /// <summary>How long a chat completion may take. The caller is waiting in silence for it.</summary>
        public static readonly TimeSpan ChatTimeout = TimeSpan.FromSeconds(20);

        /// <summary>
        /// The most models a list keeps: the cheapest, so the page's select stays a sane size while
        /// OpenRouter offers hundreds (D207).
        /// </summary>
        public const int MaxModels = 200;

        /// <summary>How long the model list may take. An admin is waiting for it, not a caller.</summary>
        public static readonly TimeSpan ModelsTimeout = TimeSpan.FromSeconds(15);

        /// <summary>The one tool the model can call: put the caller through to an extension (D203).</summary>
        public const string TransferToolName = "transfer_call";

        /// <summary>How long one reply's speech may take.</summary>
        public static readonly TimeSpan SpeakTimeout = TimeSpan.FromSeconds(15);

        /// <summary>How long one turn's transcription may take.</summary>
        public static readonly TimeSpan TranscribeTimeout = TimeSpan.FromSeconds(15);

        /// <summary>How long the voice list may take. An admin is waiting for it, not a caller.</summary>
        public static readonly TimeSpan VoicesTimeout = TimeSpan.FromSeconds(15);

        /// <summary>The order a voice's labels are shown in, when it has them (D205).</summary>
        private static readonly string[] LabelNames = { "language", "gender", "age", "accent" };

        /// <summary>The longest voice or model name, or voice label, kept; a provider's text, shown on a page.</summary>
        private const int MaxProviderTextLength = 100;

        /// <summary>The most provider error text carried into a log line.</summary>
        private const int MaxErrorTextLength = 300;

        /// <summary>
        /// The largest answer read: a few minutes of μ-law speech. A provider answering with more
        /// than that is not answering the question.
        /// </summary>
        private const int MaxResponseBytes = 4 * 1024 * 1024;

        /// <summary>Appended to <see cref="AiSettingsSnapshot.OllamaUrl"/>, which is stored without a trailing slash.</summary>
        private const string OllamaChatPath = "/api/chat";

        /// <summary>Ollama's list of the models the server has pulled, under the same base URL.</summary>
        private const string OllamaTagsPath = "/api/tags";

        private const string OpenRouterChatUri = "https://openrouter.ai/api/v1/chat/completions";
        private const string OpenRouterModelsProvider = "OpenRouter models";
        private const string OpenRouterModelsUri = "https://openrouter.ai/api/v1/models";
        private const string SpeechToTextUri = "https://api.elevenlabs.io/v1/speech-to-text";
        private const string TextToSpeechUri = "https://api.elevenlabs.io/v1/text-to-speech/";
        private const string VoicesProvider = "ElevenLabs voices";
        private const string VoicesUri = "https://api.elevenlabs.io/v1/voices";

        /// <summary>
        /// One chat completion from the chosen model provider (D204). <paramref name="transferTargets"/>
        /// are the extensions the model may put the caller through to; empty sends no tools, exactly
        /// as before D203. Both providers answer in the same <see cref="AiChatReply"/>.
        /// </summary>
        public static async Task<AiChatReply> ChatAsync(
            HttpClient http,
            AiSettingsSnapshot settings,
            string model,
            IReadOnlyList<(string Role, string Content)> messages,
            IReadOnlyList<(string Extension, string Name)> transferTargets,
            CancellationToken cancellationToken)
        {
            return settings.Provider == AiModelProviders.Ollama
                ? await OllamaChatAsync(http, settings, model, messages, transferTargets, cancellationToken)
                : await OpenRouterChatAsync(http, settings, model, messages, transferTargets, cancellationToken);
        }

        /// <summary>
        /// The tool-capable models the chosen model provider offers (D207): OpenRouter's public list,
        /// asked with the saved key when there is one, since a key can change what is listed; or
        /// the models the Ollama server has pulled. At most <see cref="MaxModels"/>, cheapest first.
        /// Every failure is an <see cref="AiProviderException"/> the page can show as it stands.
        /// </summary>
        public static async Task<List<AiModel>> ListModelsAsync(HttpClient http, AiSettingsSnapshot settings, CancellationToken cancellationToken)
        {
            if (settings.Provider == AiModelProviders.Ollama)
            {
                if (settings.OllamaUrl.Length == 0)
                    throw new AiProviderException("Set the Ollama URL first.");

                // Checked again for the same reason the chat checks it: a request is only ever
                // built from a value the Settings page would accept.
                if (!SettingsValidation.IsOllamaUrl(settings.OllamaUrl))
                    throw new AiProviderException("The Ollama URL in Settings is not a valid address. Fix it, then load the models again.");

                var uri = settings.OllamaUrl + OllamaTagsPath;
                var provider = $"Ollama models at {uri}";

                using var ollamaRequest = new HttpRequestMessage(HttpMethod.Get, uri);
                var ollamaBody = await SendAsync(http, ollamaRequest, provider, ModelsTimeout, "", cancellationToken);

                return ReadOllamaModels(ollamaBody);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, OpenRouterModelsUri);

            if (settings.OpenRouterApiKey.Length > 0)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenRouterApiKey);

            var body = await SendAsync(http, request, OpenRouterModelsProvider, ModelsTimeout, settings.OpenRouterApiKey, cancellationToken);

            return ReadOpenRouterModels(body);
        }

        /// <summary>
        /// The voices the saved ElevenLabs key may use, by category then name (D205). A refusal is
        /// an <see cref="AiProviderException"/> worded by <see cref="VoicesError"/>, so the page can
        /// show it as it stands.
        /// </summary>
        public static async Task<List<AiVoice>> ListVoicesAsync(HttpClient http, AiSettingsSnapshot settings, CancellationToken cancellationToken)
        {
            if (settings.ElevenLabsApiKey.Length == 0)
                throw new AiProviderException("Set the ElevenLabs key first.");

            using var request = new HttpRequestMessage(HttpMethod.Get, VoicesUri);
            request.Headers.Add("xi-api-key", settings.ElevenLabsApiKey);

            var body = await SendAsync(
                http,
                request,
                VoicesProvider,
                VoicesTimeout,
                settings.ElevenLabsApiKey,
                cancellationToken,
                (status, reason, error) => VoicesError(status, reason, error, settings.ElevenLabsApiKey));

            return ReadVoices(body);
        }

        /// <summary>
        /// The Ollama <c>/api/chat</c> request body (D204). The same messages and tools as
        /// OpenRouter's; what differs is <c>stream: false</c>, which Ollama needs to answer in one
        /// piece, and the limits, which live under <c>options</c> with Ollama's own names.
        /// </summary>
        public static JsonObject OllamaChatRequest(
            string model,
            IReadOnlyList<(string Role, string Content)> messages,
            IReadOnlyList<(string Extension, string Name)> transferTargets)
        {
            var request = new JsonObject
            {
                ["model"] = model,
                ["messages"] = Messages(messages),
                ["stream"] = false,
                ["options"] = new JsonObject
                {
                    ["temperature"] = 0.2,
                    ["num_predict"] = 250,
                },
            };

            if (transferTargets.Count > 0)
                request["tools"] = new JsonArray(TransferTool(transferTargets));

            return request;
        }

        /// <summary>
        /// The OpenRouter chat request body. Separate from the sending so its shape can be checked
        /// without a network: the tool, and its enum of extensions, are only there when there are
        /// targets.
        /// </summary>
        public static JsonObject OpenRouterChatRequest(
            string model,
            IReadOnlyList<(string Role, string Content)> messages,
            IReadOnlyList<(string Extension, string Name)> transferTargets)
        {
            var request = new JsonObject
            {
                ["model"] = model,
                // Low temperature because this is a receptionist, not a writer: the same question
                // should get the same answer, and invention is the failure mode that matters.
                ["temperature"] = 0.2,
                // Short replies are the point on a phone call, and the cap is what stops a model
                // from producing a paragraph nobody wants read aloud.
                ["max_tokens"] = 250,
                ["messages"] = Messages(messages),
            };

            if (transferTargets.Count > 0)
                request["tools"] = new JsonArray(TransferTool(transferTargets));

            return request;
        }

        /// <summary>
        /// Ollama's answer: <c>message</c> at the top level rather than under <c>choices</c>, and a
        /// tool call's <c>arguments</c> as a JSON object rather than a string (D204). Read into the
        /// same <see cref="AiChatReply"/> as OpenRouter's, so the session cannot tell them apart.
        /// </summary>
        public static AiChatReply ReadOllamaChatReply(byte[] body) =>
            ReadChat(body, "Ollama chat", root => root.GetProperty("message"));

        /// <summary>
        /// Ollama's <c>/api/tags</c>: the names of the models the server has pulled, by name, at most
        /// <see cref="MaxModels"/>. Ollama does not say which take tools — it either uses them or
        /// ignores them per model, which the session copes with — so every one is offered, and a
        /// local model costs nothing per token. A name the model row would refuse is left out.
        /// </summary>
        public static List<AiModel> ReadOllamaModels(byte[] body)
        {
            const string provider = "Ollama models";

            try
            {
                using var document = JsonDocument.Parse(body);
                var models = new List<AiModel>();

                foreach (var model in document.RootElement.GetProperty("models").EnumerateArray())
                {
                    var name = IdOf(model, "name");

                    if (AiModel.IsPlausibleId(name))
                        models.Add(new AiModel { Id = name, Name = name, SupportsTools = true });
                }

                return models
                    .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxModels)
                    .ToList();
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new AiProviderException($"{provider} answered with something other than the expected JSON.");
            }
        }

        /// <summary>
        /// OpenRouter's answer: the first choice's words and its first tool call, either of which
        /// may be missing. A model that answered with neither has said nothing, which is an empty
        /// reply rather than a failure.
        /// </summary>
        public static AiChatReply ReadOpenRouterChatReply(byte[] body) =>
            ReadChat(body, "OpenRouter chat", root => root.GetProperty("choices")[0].GetProperty("message"));

        /// <summary>
        /// OpenRouter's <c>/api/v1/models</c>, kept to the models that take tools — an agent whose
        /// model cannot call <c>transfer_call</c> cannot transfer (D207) — and ordered by output
        /// price then id, at most <see cref="MaxModels"/>. Prices arrive as strings of dollars per
        /// token and are kept per million. A model without a fixed price (OpenRouter writes
        /// <c>-1</c> for a router whose price depends on where it sends the request) cannot be
        /// ranked by price and is left out, as is an id the model row would refuse.
        /// </summary>
        public static List<AiModel> ReadOpenRouterModels(byte[] body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var models = new List<AiModel>();

                foreach (var model in document.RootElement.GetProperty("data").EnumerateArray())
                {
                    var id = IdOf(model, "id");

                    // A ":batch" variant answers asynchronously on a lower tier: it would be a
                    // cheap-looking choice that cannot answer a live call in time (D207).
                    if (!AiModel.IsPlausibleId(id) || !TakesTools(model) ||
                        id.EndsWith(":batch", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!model.TryGetProperty("pricing", out var pricing) || pricing.ValueKind != JsonValueKind.Object ||
                        PerMillion(pricing, "prompt") is not { } input ||
                        PerMillion(pricing, "completion") is not { } output)
                        continue;

                    var name = StringOf(model, "name");

                    models.Add(new AiModel
                    {
                        ContextLength = model.TryGetProperty("context_length", out var context) && context.ValueKind == JsonValueKind.Number && context.TryGetInt32(out var length) ? length : 0,
                        Id = id,
                        InputPricePerMillion = input,
                        Name = name.Length > 0 ? name : id,
                        OutputPricePerMillion = output,
                        SupportsTools = true,
                    });
                }

                return models
                    .OrderBy(m => m.OutputPricePerMillion)
                    .ThenBy(m => m.Id, StringComparer.Ordinal)
                    .Take(MaxModels)
                    .ToList();
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new AiProviderException($"{OpenRouterModelsProvider} answered with something other than the expected JSON.");
            }
        }

        /// <summary>
        /// ElevenLabs' v1 voice list, by category then name. A voice with no labels has a blank
        /// <see cref="AiVoice.Labels"/>; one whose ID is not shaped like a voice ID is left out,
        /// because the page would refuse to save it. Names and labels are flattened to one line and
        /// cut short, but otherwise the provider's text: the page renders them as text.
        /// </summary>
        public static List<AiVoice> ReadVoices(byte[] body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var voices = new List<AiVoice>();

                foreach (var voice in document.RootElement.GetProperty("voices").EnumerateArray())
                {
                    var id = StringOf(voice, "voice_id");

                    if (!AiVoice.IsPlausibleId(id))
                        continue;

                    var labels = voice.TryGetProperty("labels", out var found) && found.ValueKind == JsonValueKind.Object
                        ? LabelNames.Select(name => StringOf(found, name)).Where(text => text.Length > 0)
                        : [];

                    var name = StringOf(voice, "name");

                    voices.Add(new AiVoice
                    {
                        Category = StringOf(voice, "category"),
                        Id = id,
                        Labels = string.Join(" · ", labels),
                        Name = name.Length > 0 ? name : id,
                    });
                }

                return voices
                    .OrderBy(v => v.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new AiProviderException($"{VoicesProvider} answered with something other than the expected JSON.");
            }
        }

        /// <summary>The reply's speech as μ-law 8 kHz bytes, exactly what <c>ulaw_8000</c> returns.</summary>
        public static async Task<byte[]> SpeakAsync(
            HttpClient http,
            AiSettingsSnapshot settings,
            string model,
            string voiceId,
            string text,
            CancellationToken cancellationToken)
        {
            const string provider = "ElevenLabs text-to-speech";

            if (voiceId.Length == 0)
                throw new AiProviderException($"{provider}: no voice is set, on the agent or in Settings.");

            var uri = TextToSpeechUri + Uri.EscapeDataString(voiceId) + "?output_format=ulaw_8000";

            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(new { text, model_id = model }),
            };
            request.Headers.Add("xi-api-key", settings.ElevenLabsApiKey);

            return await SendAsync(http, request, provider, SpeakTimeout, settings.ElevenLabsApiKey, cancellationToken);
        }

        /// <summary>What the caller said, from one turn's WAV. Empty when the provider heard nothing.</summary>
        public static async Task<string> TranscribeAsync(
            HttpClient http,
            AiSettingsSnapshot settings,
            byte[] wav,
            CancellationToken cancellationToken)
        {
            const string provider = "ElevenLabs speech-to-text";

            var file = new ByteArrayContent(wav);
            file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

            var form = new MultipartFormDataContent
            {
                { file, "file", "turn.wav" },
                { new StringContent(settings.SttModel), "model_id" },
                { new StringContent("false"), "tag_audio_events" },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, SpeechToTextUri) { Content = form };
            request.Headers.Add("xi-api-key", settings.ElevenLabsApiKey);

            var body = await SendAsync(http, request, provider, TranscribeTimeout, settings.ElevenLabsApiKey, cancellationToken);

            return ReadJson(body, provider, root => root.GetProperty("text").GetString());
        }

        /// <summary>
        /// The <see cref="TransferToolName"/> function. The extension is an enum, so a model that
        /// follows the schema cannot name one it was not given, and the description carries the
        /// names ("101 Jaysam, 102 Anishi") so it can match a name the caller says to a number.
        /// The session checks the answer against the same list anyway (D203).
        /// </summary>
        public static JsonObject TransferTool(IReadOnlyList<(string Extension, string Name)> transferTargets)
        {
            var staff = string.Join(", ", transferTargets.Select(t => $"{t.Extension} {t.Name}"));

            return new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = TransferToolName,
                    ["description"] =
                        "Transfer the caller to a member of staff by extension. Use this only when the caller asks to " +
                        $"speak to a person. Staff: {staff}.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["extension"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["enum"] = new JsonArray(transferTargets.Select(t => (JsonNode)JsonValue.Create(t.Extension)!).ToArray()),
                                ["description"] = "The extension to transfer to",
                            },
                        },
                        ["required"] = new JsonArray("extension"),
                    },
                },
            };
        }

        /// <summary>
        /// A refused voice list as a sentence an admin can act on (D205), rather than the
        /// provider's JSON. The two refusals that matter are named: a key without the
        /// <c>voices_read</c> permission, and a 402 (the free plan's "cannot use library voices via
        /// the API"), which keeps the provider's own reason. Anything else is the status and the
        /// provider's text, as every other provider failure is.
        /// </summary>
        public static string VoicesError(int status, string? reason, byte[] body, string key)
        {
            var text = ErrorText(body, key);

            if (status == 401 && text.Contains("missing_permissions", StringComparison.Ordinal))
                return "The ElevenLabs key cannot read voices: it needs the voices_read permission. Add it to the key, or type the voice ID by hand.";

            if (status == 402)
                return $"ElevenLabs refused the request: {ProviderMessage(body, key, text)}";

            return $"{VoicesProvider} answered {status} {reason}: {text}";
        }

        /// <summary>
        /// Text from a provider as one short line: control characters flattened, cut to length,
        /// and the key scrubbed out in case a provider ever echoes it back.
        /// </summary>
        private static string Clean(string text, string key, int maxLength)
        {
            text = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();

            if (key.Length > 0)
                text = text.Replace(key, "[key]", StringComparison.Ordinal);

            return text.Length > maxLength ? text[..maxLength] : text;
        }

        /// <summary>The provider's error body as one short line, by <see cref="Clean"/>.</summary>
        private static string ErrorText(byte[] body, string key) =>
            Clean(Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, MaxErrorTextLength * 4)), key, MaxErrorTextLength);

        /// <summary>
        /// A model id as the provider sent it, or blank: not cut short like <see cref="StringOf"/>,
        /// because a shortened id would be a different model. The caller checks its shape.
        /// </summary>
        private static string IdOf(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

        /// <summary>The conversation as both providers take it: an array of role and content.</summary>
        private static JsonArray Messages(IReadOnlyList<(string Role, string Content)> messages) =>
            new(messages.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = m.Content }).ToArray());

        private static async Task<AiChatReply> OllamaChatAsync(
            HttpClient http,
            AiSettingsSnapshot settings,
            string model,
            IReadOnlyList<(string Role, string Content)> messages,
            IReadOnlyList<(string Extension, string Name)> transferTargets,
            CancellationToken cancellationToken)
        {
            // The snapshot reads what is stored without judging it, so the URL is checked again
            // here: a request is only ever built from a value the Settings page would accept.
            if (settings.OllamaUrl.Length == 0 || !SettingsValidation.IsOllamaUrl(settings.OllamaUrl))
                throw new AiProviderException("Ollama chat: no valid Ollama URL is set in Settings.");

            var uri = settings.OllamaUrl + OllamaChatPath;

            // The URL is the admin's own server and carries no credential, so naming it in every
            // error is what tells them which address did not answer.
            var provider = $"Ollama chat at {uri}";

            if (model.Length == 0)
                throw new AiProviderException($"{provider}: no model is set, on the agent or in Settings.");

            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(OllamaChatRequest(model, messages, transferTargets)),
            };

            var body = await SendAsync(http, request, provider, ChatTimeout, "", cancellationToken);

            return ReadOllamaChatReply(body);
        }

        private static async Task<AiChatReply> OpenRouterChatAsync(
            HttpClient http,
            AiSettingsSnapshot settings,
            string model,
            IReadOnlyList<(string Role, string Content)> messages,
            IReadOnlyList<(string Extension, string Name)> transferTargets,
            CancellationToken cancellationToken)
        {
            const string provider = "OpenRouter chat";

            if (model.Length == 0)
                throw new AiProviderException($"{provider}: no model is set, on the agent or in Settings.");

            using var request = new HttpRequestMessage(HttpMethod.Post, OpenRouterChatUri)
            {
                Content = JsonContent.Create(OpenRouterChatRequest(model, messages, transferTargets)),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenRouterApiKey);

            var body = await SendAsync(http, request, provider, ChatTimeout, settings.OpenRouterApiKey, cancellationToken);

            return ReadOpenRouterChatReply(body);
        }

        /// <summary>
        /// A price string of OpenRouter's, dollars per token, as dollars per million; null when it
        /// is absent, not a number, or negative (a price that is not fixed).
        /// </summary>
        private static decimal? PerMillion(JsonElement pricing, string name)
        {
            if (!pricing.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
                !decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var perToken) ||
                perToken < 0)
                return null;

            return perToken * 1_000_000m;
        }

        /// <summary>
        /// ElevenLabs' own reason from an error body, <c>{"detail":{"message":"…"}}</c> or
        /// <c>{"detail":"…"}</c>, cleaned like any error text; <paramref name="fallback"/> when the
        /// body has neither.
        /// </summary>
        private static string ProviderMessage(byte[] body, string key, string fallback)
        {
            try
            {
                using var document = JsonDocument.Parse(body);

                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("detail", out var detail))
                    return fallback;

                var message = detail.ValueKind == JsonValueKind.String ? detail.GetString() ?? ""
                    : detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() ?? ""
                    : "";

                return message.Length > 0 ? Clean(message, key, MaxErrorTextLength) : fallback;
            }
            catch (JsonException)
            {
                return fallback;
            }
        }

        private static async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, string provider, CancellationToken cancellationToken)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;

            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxResponseBytes)
                    throw new AiProviderException($"{provider} answered with more than {MaxResponseBytes} bytes.");

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }

        /// <summary>
        /// One chat answer, wherever the provider keeps its message. Its first tool call's
        /// arguments are kept as JSON text whichever way they arrived — OpenAI's shape is a string
        /// holding the JSON, Ollama's is the object itself (D204) — so
        /// <see cref="AiToolCall.Argument"/> reads both the same way.
        /// </summary>
        private static AiChatReply ReadChat(byte[] body, string provider, Func<JsonElement, JsonElement> findMessage)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var message = findMessage(document.RootElement);

                var content = message.TryGetProperty("content", out var text) && text.ValueKind == JsonValueKind.String
                    ? text.GetString() ?? ""
                    : "";

                AiToolCall? toolCall = null;

                if (message.TryGetProperty("tool_calls", out var calls) &&
                    calls.ValueKind == JsonValueKind.Array &&
                    calls.GetArrayLength() > 0)
                {
                    var function = calls[0].GetProperty("function");
                    var name = function.GetProperty("name").GetString() ?? "";

                    var arguments = !function.TryGetProperty("arguments", out var args) ? "{}"
                        : args.ValueKind == JsonValueKind.String ? args.GetString() ?? "{}"
                        : args.GetRawText();

                    toolCall = new AiToolCall(name, arguments);
                }

                return new AiChatReply { Content = content, ToolCall = toolCall };
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                throw new AiProviderException($"{provider} answered with something other than the expected JSON.");
            }
        }

        private static string ReadJson(byte[] body, string provider, Func<JsonElement, string?> read)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                return read(document.RootElement)
                    ?? throw new AiProviderException($"{provider} answered without the expected text.");
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                throw new AiProviderException($"{provider} answered with something other than the expected JSON.");
            }
        }

        private static async Task<byte[]> SendAsync(
            HttpClient http,
            HttpRequestMessage request,
            string provider,
            TimeSpan timeout,
            string key,
            CancellationToken cancellationToken,
            Func<int, string?, byte[], string>? describeFailure = null)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
                var body = await ReadBodyAsync(response, provider, timeoutSource.Token);

                if (!response.IsSuccessStatusCode)
                {
                    throw new AiProviderException(describeFailure != null
                        ? describeFailure((int)response.StatusCode, response.ReasonPhrase, body)
                        : $"{provider} answered {(int)response.StatusCode} {response.ReasonPhrase}: {ErrorText(body, key)}");
                }

                return body;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AiProviderException($"{provider} did not answer within {timeout.TotalSeconds:0} seconds.");
            }
            catch (HttpRequestException ex)
            {
                throw new AiProviderException($"{provider} could not be reached ({ex.HttpRequestError}).");
            }
        }

        /// <summary>A string property of a provider's object, flattened and cut short, or blank when absent or not a string.</summary>
        private static string StringOf(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? Clean(value.GetString() ?? "", "", MaxProviderTextLength)
                : "";

        /// <summary>Whether an OpenRouter model lists <c>tools</c> among its supported parameters.</summary>
        private static bool TakesTools(JsonElement model) =>
            model.TryGetProperty("supported_parameters", out var parameters) &&
            parameters.ValueKind == JsonValueKind.Array &&
            parameters.EnumerateArray().Any(p => p.ValueKind == JsonValueKind.String && p.GetString() == "tools");
    }
}
