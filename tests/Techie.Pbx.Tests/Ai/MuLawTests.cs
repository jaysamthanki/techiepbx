using Techie.Pbx.Core.Ai;

namespace Techie.Pbx.Tests.Ai
{
    /// <summary>G.711 μ-law, the format ElevenLabs' <c>ulaw_8000</c> answers in (D200).</summary>
    public class MuLawTests
    {
        [Theory]
        [InlineData(0, 0xFF)]
        [InlineData(32767, 0x80)]
        [InlineData(-32768, 0x00)]
        [InlineData(-1, 0x7F)]
        [InlineData(1000, 0xCE)]
        public void Known_samples_encode_to_the_standard_bytes(short sample, byte expected)
        {
            Assert.Equal(expected, MuLaw.Encode(sample));
        }

        [Theory]
        [InlineData(0xFF, 0)]
        [InlineData(0x7F, 0)]
        [InlineData(0x80, 32124)]
        [InlineData(0x00, -32124)]
        public void Known_bytes_decode_to_the_standard_samples(byte value, short expected)
        {
            Assert.Equal(expected, MuLaw.Decode(value));
        }

        [Fact]
        public void Silence_is_0xFF()
        {
            Assert.All(MuLaw.ToMuLaw(new short[160]), b => Assert.Equal(0xFF, b));
        }

        [Fact]
        public void Encode_then_decode_stays_within_the_quantisation_step()
        {
            for (int sample = short.MinValue; sample <= short.MaxValue; sample += 7)
            {
                var decoded = MuLaw.Decode(MuLaw.Encode((short)sample));
                var tolerance = Math.Max(16, Math.Abs(sample) / 16);

                Assert.True(Math.Abs(decoded - sample) <= tolerance, $"{sample} came back as {decoded}");
            }
        }

        [Fact]
        public void The_span_helpers_match_the_single_value_functions()
        {
            var samples = new short[] { 0, 100, -100, 20000, -20000 };

            var encoded = MuLaw.ToMuLaw(samples);
            var decoded = MuLaw.FromMuLaw(encoded);

            Assert.Equal(samples.Select(MuLaw.Encode), encoded);
            Assert.Equal(encoded.Select(MuLaw.Decode), decoded);
        }
    }
}
