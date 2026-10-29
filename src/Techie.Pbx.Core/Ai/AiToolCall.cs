using System.Text.Json;

namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// A function the model asked to have called (D203): its name, and its arguments as the JSON
    /// text the model wrote. The arguments are the model's, so nothing in them is trusted: the
    /// one tool there is checks its extension against the list it was offered.
    /// </summary>
    public sealed record AiToolCall(string Name, string Arguments)
    {
        /// <summary>
        /// One string argument by name, or null when the arguments are not a JSON object or do
        /// not carry it as a string.
        /// </summary>
        public string? Argument(string name)
        {
            try
            {
                using var document = JsonDocument.Parse(this.Arguments);

                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }

                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
