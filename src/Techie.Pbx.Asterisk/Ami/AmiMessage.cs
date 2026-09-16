namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// One packet received from Asterisk: a response to an action, or an event. Headers keep
    /// their order and can repeat, which some events rely on.
    /// </summary>
    public class AmiMessage
    {
        private readonly List<KeyValuePair<string, string>> _headers;

        public AmiMessage(IEnumerable<KeyValuePair<string, string>> headers)
        {
            _headers = headers.ToList();
        }

        public IReadOnlyList<KeyValuePair<string, string>> Headers => _headers;

        public string? Response => Get("Response");
        public string? EventName => Get("Event");
        public string? ActionID => Get("ActionID");
        public string? Message => Get("Message");

        public bool IsSuccess => string.Equals(Response, "Success", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The first value for a header name, or null. Header names are case insensitive.
        /// </summary>
        public string? Get(string name)
        {
            foreach (var header in _headers)
            {
                if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                    return header.Value;
            }

            return null;
        }

        /// <summary>
        /// Every value for a header name, in order.
        /// </summary>
        public List<string> GetAll(string name) => _headers
            .Where(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Value)
            .ToList();

        /// <summary>
        /// Deliberately short: log lines should identify a message, not dump its headers.
        /// </summary>
        public override string ToString() =>
            EventName != null ? $"Event: {EventName}" : $"Response: {Response}";
    }
}
