using System.Buffers.Binary;
using System.Text;

namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// Wraps a caller's utterance in a WAV file for speech-to-text (D200): the canonical 44-byte
    /// RIFF header for 16-bit mono PCM, then the samples, little-endian.
    /// </summary>
    public static class WavWriter
    {
        public const int HeaderLength = 44;

        public static byte[] ToWav(ReadOnlySpan<short> pcm16, int sampleRateHz)
        {
            var dataLength = pcm16.Length * 2;
            var wav = new byte[HeaderLength + dataLength];
            var span = wav.AsSpan();

            Encoding.ASCII.GetBytes("RIFF", span);
            BinaryPrimitives.WriteInt32LittleEndian(span[4..], HeaderLength - 8 + dataLength);
            Encoding.ASCII.GetBytes("WAVE", span[8..]);
            Encoding.ASCII.GetBytes("fmt ", span[12..]);
            BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
            BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
            BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
            BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRateHz);
            BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRateHz * 2);
            BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
            BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
            Encoding.ASCII.GetBytes("data", span[36..]);
            BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataLength);

            for (var i = 0; i < pcm16.Length; i++)
                BinaryPrimitives.WriteInt16LittleEndian(span[(HeaderLength + i * 2)..], pcm16[i]);

            return wav;
        }
    }
}
