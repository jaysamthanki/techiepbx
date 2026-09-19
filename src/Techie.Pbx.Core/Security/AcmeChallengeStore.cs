using System.Collections.Concurrent;

namespace Techie.Pbx.Core.Security
{
    /// <summary>
    /// The answers to the HTTP-01 challenges of an order in flight (D98). An ACME server asks
    /// <c>http://&lt;host&gt;/.well-known/acme-challenge/&lt;token&gt;</c> and expects the key
    /// authorization back as plain text; this is where the order puts them and where the endpoint
    /// that answers reads them from.
    ///
    /// In memory and short-lived on purpose. A token is only meaningful for the minute or two an
    /// order takes, so there is nothing to store, nothing to clean up on restart, and no file for
    /// anything else to serve by accident. Static because the endpoint answering the challenge and
    /// the order that created it are in the same process but have no other way to meet — the app
    /// answers its own challenges rather than writing them into a web root.
    /// </summary>
    public static class AcmeChallengeStore
    {
        private static readonly ConcurrentDictionary<string, string> Answers = new(StringComparer.Ordinal);

        /// <summary>How many challenges are waiting to be answered. For the log, and for tests.</summary>
        public static int Count => Answers.Count;

        /// <summary>Publishes the answer to one token, replacing any previous answer for it.</summary>
        public static void Add(string token, string keyAuthorization)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("An ACME challenge token is required.", nameof(token));

            Answers[token] = keyAuthorization;
        }

        /// <summary>
        /// The answer to one token, or null when nothing is expecting it. A token nobody published
        /// is not an error: it is a scanner walking the well-known path, and it gets a 404.
        /// </summary>
        public static string? Answer(string token) =>
            token.Length > 0 && Answers.TryGetValue(token, out var answer) ? answer : null;

        /// <summary>Forgets every token. An order clears up after itself, successful or not.</summary>
        public static void Clear() => Answers.Clear();

        /// <summary>Forgets one token.</summary>
        public static void Remove(string token) => Answers.TryRemove(token, out _);
    }
}
