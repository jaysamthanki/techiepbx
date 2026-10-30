using Techie.Pbx.Core.Ai;

namespace Techie.Pbx.Tests.Ai
{
    /// <summary>
    /// The voice list cache (D205): one provider call per key per lifetime, shared while loading,
    /// and a failure kept only briefly. The cache is static, so each test uses keys of its own.
    /// </summary>
    public class AiVoiceCacheTests
    {
        private static readonly DateTimeOffset Start = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

        private static List<AiVoice> OneVoice(string name) => new() { new AiVoice { Id = "AAAAAAAAAAAA", Name = name } };

        private static string Key() => "cache-test-" + Guid.NewGuid().ToString("N");

        [Fact]
        public async Task Two_lookups_inside_the_window_load_once()
        {
            var key = Key();
            var calls = 0;
            Func<Task<List<AiVoice>>> load = () => { calls++; return Task.FromResult(OneVoice("first")); };

            await AiVoiceCache.GetOrAdd(key, Start, load);
            var second = await AiVoiceCache.GetOrAdd(key, Start + TimeSpan.FromMinutes(9), load);

            Assert.Equal(1, calls);
            Assert.Equal("first", second[0].Name);
        }

        [Fact]
        public async Task A_lookup_after_the_window_loads_again()
        {
            var key = Key();
            var calls = 0;
            Func<Task<List<AiVoice>>> load = () => { calls++; return Task.FromResult(OneVoice($"load {calls}")); };

            await AiVoiceCache.GetOrAdd(key, Start, load);
            var later = await AiVoiceCache.GetOrAdd(key, Start + AiVoiceCache.Lifetime, load);

            Assert.Equal(2, calls);
            Assert.Equal("load 2", later[0].Name);
        }

        [Fact]
        public async Task A_different_key_is_a_different_entry()
        {
            var calls = 0;
            Func<Task<List<AiVoice>>> load = () => { calls++; return Task.FromResult(OneVoice("x")); };

            await AiVoiceCache.GetOrAdd(Key(), Start, load);
            await AiVoiceCache.GetOrAdd(Key(), Start, load);

            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task Concurrent_lookups_share_one_load()
        {
            var key = Key();
            var calls = 0;
            var pending = new TaskCompletionSource<List<AiVoice>>();
            Func<Task<List<AiVoice>>> load = () => { Interlocked.Increment(ref calls); return pending.Task; };

            // StartNew does not unwrap, so this waits for every lookup to have returned its task
            // while the load is still unfinished: none of them can have found a cached result.
            var lookups = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => Task.Factory.StartNew(() => AiVoiceCache.GetOrAdd(key, Start, load), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default)));

            pending.SetResult(OneVoice("shared"));
            var results = await Task.WhenAll(lookups);

            Assert.Equal(1, calls);
            Assert.All(results, r => Assert.Equal("shared", r[0].Name));
        }

        [Fact]
        public async Task A_failure_is_kept_briefly_then_tried_again()
        {
            var key = Key();
            var calls = 0;
            Func<Task<List<AiVoice>>> load = () =>
            {
                calls++;
                return calls == 1 ? throw new AiProviderException("refused") : Task.FromResult(OneVoice("fixed"));
            };

            await Assert.ThrowsAsync<AiProviderException>(() => AiVoiceCache.GetOrAdd(key, Start, load));
            await Assert.ThrowsAsync<AiProviderException>(() => AiVoiceCache.GetOrAdd(key, Start + TimeSpan.FromSeconds(30), load));
            Assert.Equal(1, calls);

            var after = await AiVoiceCache.GetOrAdd(key, Start + AiVoiceCache.FailureLifetime, load);

            Assert.Equal(2, calls);
            Assert.Equal("fixed", after[0].Name);
        }
    }
}
