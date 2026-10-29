using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// The three provider calls one turn makes (D200): ElevenLabs speech-to-text, an OpenRouter
    /// chat completion, ElevenLabs text-to-speech in <c>ulaw_8000</c>. Plain request/response
    /// over the one shared <see cref="HttpClient"/>, each with its own timeout, because a caller
    /// on the line cannot wait for a provider that has stopped answering.
    ///
    /// Every failure — a status code, a timeout, a network error, an answer that is not what was
    /// asked for — is an <see cref="AiProviderException"/> whose message names no key.
    /// </summary>
    public static class AiProviders
    {
        /// <summary>How long a chat completion may take. The caller is waiting in silence for it.</summary>
        public static readonly TimeSpan ChatTimeout = TimeSpan.FromSeconds(20);

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

        private const string ChatUri = "https://openrouter.ai/api/v1/chat/completions";
        private const string SpeechToTextUri = "https://api.elevenlabs.io/v1/speech-to-text";
        private const string TextToSpeechUri = "https://api.elevenlabs.io/v1/text-to-speech/";

        public static async Task<string> ChatAsync(
            HttpClient http,
            AiSettingsSnapshot settings,
            string model,
            IReadOnlyList<(string Role, string Content)> messages,
            CancellationToken cancellationToken)
        {
            const string provider = "OpenRouter chat";

            if (model.Length == 0)
                throw new AiProviderException($"{provider}: no model is set, on the agent or in Settings.");

            using var request = new HttpRequestMessage(HttpMethod.Post, ChatUri)
            {
                Content = JsonContent.Create(new
                {
                    model,
                    // Low temperature because this is a receptionist, not a writer: the same question
                    // should get the same answer, and invention is the failure mode that matters.
                    temperature = 0.2,
                    // Short replies are the point on a phone call, and the cap is what stops a model
                    // from producing a paragraph nobody wants read aloud.
                    max_tokens = 250,
                    messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenRouterApiKey);

            var body = await SendAsync(http, request, provider, ChatTimeout, settings.OpenRouterApiKey, cancellationToken);

            return ReadJson(body, provider, root =>
                root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
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
