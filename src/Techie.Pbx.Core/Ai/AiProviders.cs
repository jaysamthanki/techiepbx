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

        /// <summary>The one tool the model can call: put the caller through to an extension (D203).</summary>
        public const string TransferToolName = "transfer_call";

        /// <summary>How long one reply's speech may take.</summary>
        public static readonly TimeSpan SpeakTimeout = TimeSpan.FromSeconds(15);

        /// <summary>How long one turn's transcription may take.</summary>
        public static readonly TimeSpan TranscribeTimeout = TimeSpan.FromSeconds(15);

        /// <summary>The most provider error text carried into a log line.</summary>
        private const int MaxErrorTextLength = 300;

        /// <summary>
        /// The largest answer read: a few minutes of μ-law speech. A provider answering with more
        /// than that is not answering the question.
        /// </summary>
        private const int MaxResponseBytes = 4 * 1024 * 1024;

        /// <summary>Appended to <see cref="AiSettingsSnapshot.OllamaUrl"/>, which is stored without a trailing slash.</summary>
        private const string OllamaChatPath = "/api/chat";

        private const string OpenRouterChatUri = "https://openrouter.ai/api/v1/chat/completions";
        private const string SpeechToTextUri = "https://api.elevenlabs.io/v1/speech-to-text";
        private const string TextToSpeechUri = "https://api.elevenlabs.io/v1/text-to-speech/";

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
        /// OpenRouter's answer: the first choice's words and its first tool call, either of which
        /// may be missing. A model that answered with neither has said nothing, which is an empty
        /// reply rather than a failure.
        /// </summary>
        public static AiChatReply ReadOpenRouterChatReply(byte[] body) =>
            ReadChat(body, "OpenRouter chat", root => root.GetProperty("choices")[0].GetProperty("message"));

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
        /// The provider's error body as one short line: control characters flattened, cut to
        /// length, and the key scrubbed out in case a provider ever echoes it back.
        /// </summary>
        private static string ErrorText(byte[] body, string key)
        {
            var text = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, MaxErrorTextLength * 4));
            text = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();

            if (key.Length > 0)
                text = text.Replace(key, "[key]", StringComparison.Ordinal);

            return text.Length > MaxErrorTextLength ? text[..MaxErrorTextLength] : text;
        }

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
            CancellationToken cancellationToken)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
                var body = await ReadBodyAsync(response, provider, timeoutSource.Token);

                if (!response.IsSuccessStatusCode)
                    throw new AiProviderException($"{provider} answered {(int)response.StatusCode} {response.ReasonPhrase}: {ErrorText(body, key)}");

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
    }
}
