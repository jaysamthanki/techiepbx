using System.Buffers.Binary;
using System.Text;
using Techie.Pbx.Core.Ai;

namespace Techie.Pbx.Tests.Ai
{
    /// <summary>The WAV a turn is sent to speech-to-text in (D200): 44-byte RIFF header, 16-bit mono PCM.</summary>
    public class WavWriterTests
    {
        [Fact]
        public void The_header_describes_16_bit_mono_pcm_at_the_given_rate()
        {
            var wav = WavWriter.ToWav(new short[100], 8000);

            Assert.Equal(44 + 200, wav.Length);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
            Assert.Equal(wav.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4)));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
            Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
            Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(16)));
            Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20)));
            Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)));
            Assert.Equal(8000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
            Assert.Equal(16000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(28)));
            Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(32)));
            Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34)));
            Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
            Assert.Equal(200, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
        }

        [Fact]
        public void Samples_follow_the_header_little_endian()
        {
            var wav = WavWriter.ToWav(new short[] { 0x0102, -2 }, 8000);

            Assert.Equal(new byte[] { 0x02, 0x01, 0xFE, 0xFF }, wav[44..]);
        }

        [Fact]
        public void No_samples_is_just_the_header()
        {
            Assert.Equal(WavWriter.HeaderLength, WavWriter.ToWav([], 8000).Length);
        }
    }
}
