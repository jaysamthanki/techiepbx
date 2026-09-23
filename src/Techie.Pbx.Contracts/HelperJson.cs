using System.Text.Json;
using System.Text.Json.Serialization;

namespace Techie.Pbx.Contracts
{
    /// <summary>
    /// How the two ends of the socket agree to write JSON: camelCase names, lowercase enum names,
    /// and nulls left out. One request object, one reply object, each on its own line, then the
    /// connection closes (D142) — newline delimited rather than length prefixed because a
    /// protocol you can read with <c>socat</c> is a protocol you can debug on the box.
    ///
    /// <see cref="Serializer"/> never emits a raw newline: a control character inside a string is
    /// escaped, so one object really is one line.
    /// </summary>
    public static class HelperJson
    {
        /// <summary>
        /// Integer enum values are refused on purpose. A message saying <c>"protocol": 1</c> is
        /// not a message this protocol defines, and accepting it would mean the wire format and
        /// the C# member values had to stay in step forever.
        /// </summary>
        public static readonly JsonSerializerOptions Serializer = new()
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        /// <summary>
        /// One message as it goes on the wire, without its newline. Throws on anything that
        /// cannot be written, which is a bug in our own code rather than something a caller did.
        /// </summary>
        public static string Line<T>(T message) => JsonSerializer.Serialize(message, Serializer);

        /// <summary>
        /// One line back into a message, or null when it is not one. Malformed JSON, an unknown
        /// protocol name and a string where a number belongs all land here as null, and the
        /// caller answers with an error reply — the Helper never throws its way out of a bad
        /// message (D142).
        /// </summary>
        public static T? Parse<T>(string line) where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(line, Serializer);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
