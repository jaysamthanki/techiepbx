namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// A provider's list per credential, so the AI settings page does not ask the provider every
    /// time an admin presses "Load voices" (D205) or "Load models" (D207). One cache per item type,
    /// so voices and models never share an entry. Keyed by what decides the list — the ElevenLabs
    /// key for voices, the provider plus its key or URL for models; a key is only ever a dictionary
    /// key here, never logged or shown.
    ///
    /// A list is kept for <see cref="Lifetime"/>. A failed load is kept too, for the shorter
    /// <see cref="FailureLifetime"/>: a key without <c>voices_read</c> fails the same way every
    /// time, and caching the failure briefly stops a page of repeated clicks from hammering the
    /// provider, while a fixed key is picked up within a minute. A load still in flight is shared,
    /// so two lookups for the same key never start two requests.
    /// </summary>
    public static class AiListCache<T>
    {
        /// <summary>How long a failed load is answered from the cache.</summary>
        public static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(1);

        /// <summary>How long a list is answered from the cache.</summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

        private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);

        private static readonly object Gate = new();

        /// <summary>Forgets every entry. For tests, which share this one static cache.</summary>
        public static void Clear()
        {
            lock (Gate)
                Entries.Clear();
        }

        /// <summary>
        /// The list for <paramref name="key"/>: the cached list while it is fresh at
        /// <paramref name="now"/>, the load already running if there is one, and otherwise a new
        /// call to <paramref name="load"/>. A failed load's exception is what the caller awaits.
        /// </summary>
        public static Task<List<T>> GetOrAdd(string key, DateTimeOffset now, Func<Task<List<T>>> load)
        {
            Lazy<Task<List<T>>> lazy;

            lock (Gate)
            {
                Prune(now);

                if (!Entries.TryGetValue(key, out var entry))
                {
                    // Wrapped, so a loader that throws before its first await still fails as a
                    // task (which expires) rather than as a Lazy that rethrows for ever.
                    entry = new Entry(now, new Lazy<Task<List<T>>>(async () => await load()));
                    Entries[key] = entry;
                }

                lazy = entry.Load;
            }

            // Outside the lock: the loader starts a request, and Lazy's own locking makes sure it
            // is started once however many callers arrive together.
            return lazy.Value;
        }

        /// <summary>
        /// Drops every entry that is no longer fresh at <paramref name="now"/>, so an old key's list
        /// does not outlive its usefulness. An entry still loading stays: its callers are waiting.
        /// </summary>
        private static void Prune(DateTimeOffset now)
        {
            var stale = Entries.Where(e => !e.Value.IsFresh(now)).Select(e => e.Key).ToList();

            foreach (var key in stale)
                Entries.Remove(key);
        }

        private sealed record Entry(DateTimeOffset Started, Lazy<Task<List<T>>> Load)
        {
            public bool IsFresh(DateTimeOffset now)
            {
                if (!this.Load.IsValueCreated || !this.Load.Value.IsCompleted)
                    return true;

                var lifetime = this.Load.Value.IsCompletedSuccessfully ? Lifetime : FailureLifetime;

                return now < this.Started + lifetime;
            }
        }
    }
}
