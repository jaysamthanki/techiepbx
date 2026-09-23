using System.Text.Json;

namespace Techie.Pbx.Contracts
{
    /// <summary>
    /// The one object the Helper sends back: it worked and here is the result, or it did not and
    /// here is a sentence saying why. The sentence is meant to be shown to an admin as it is —
    /// the web side never invents its own wording for a helper failure, because the Helper is the
    /// only end that knows what nft said.
    /// </summary>
    public class HelperReply
    {
        /// <summary>Why it did not work, in one human sentence. Null when it did.</summary>
        public string? Error { get; set; }

        public bool Ok { get; set; }

        /// <summary>
        /// The reply's payload, kept as raw JSON so that this class does not have to know every
        /// result type. <see cref="ResultAs{T}"/> is how a caller reads it.
        /// </summary>
        public JsonElement? Result { get; set; }

        public static HelperReply Failed(string error) => new() { Error = error, Ok = false };

        public static HelperReply Succeeded<T>(T result) => new()
        {
            Ok = true,
            Result = JsonSerializer.SerializeToElement(result, HelperJson.Serializer),
        };

        /// <summary>
        /// The payload as the type this message is documented to carry. A reply with no result at
        /// all, or one that is not that shape, is an error rather than a default-constructed
        /// object: a caller that acted on an empty status would show an empty table and call it
        /// the truth.
        /// </summary>
        public T ResultAs<T>() where T : class
        {
            if (this.Result == null)
                throw new InvalidOperationException("The helper replied without a result.");

            return this.Result.Value.Deserialize<T>(HelperJson.Serializer)
                ?? throw new InvalidOperationException("The helper's result could not be read.");
        }
    }
}
