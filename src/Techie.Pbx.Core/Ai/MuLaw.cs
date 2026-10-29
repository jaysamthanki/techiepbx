namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// G.711 μ-law, the format ElevenLabs returns as <c>ulaw_8000</c> (D200). Decoding goes through
    /// a 256-entry table; encoding is the standard biased-segment calculation.
    /// </summary>
    public static class MuLaw
    {
        private const int Bias = 0x84;
        private const int Clip = 32635;

        private static readonly short[] DecodeTable = BuildDecodeTable();

        public static short Decode(byte value) => DecodeTable[value];

        public static byte Encode(short sample)
        {
            int value = sample;
            var sign = value < 0 ? 0x80 : 0;

            if (sign != 0)
                value = -value;

            if (value > Clip)
                value = Clip;

            value += Bias;

            var exponent = 7;
            for (var mask = 0x4000; (value & mask) == 0 && exponent > 0; mask >>= 1)
                exponent--;

            var mantissa = (value >> (exponent + 3)) & 0x0F;

            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        public static short[] FromMuLaw(ReadOnlySpan<byte> encoded)
        {
            var samples = new short[encoded.Length];

            for (var i = 0; i < encoded.Length; i++)
                samples[i] = DecodeTable[encoded[i]];

            return samples;
        }

        public static byte[] ToMuLaw(ReadOnlySpan<short> samples)
        {
            var encoded = new byte[samples.Length];

            for (var i = 0; i < samples.Length; i++)
                encoded[i] = Encode(samples[i]);

            return encoded;
        }

        private static short[] BuildDecodeTable()
        {
            var table = new short[256];

            for (var i = 0; i < 256; i++)
            {
                var value = ~i & 0xFF;
                var exponent = (value >> 4) & 0x07;
                var magnitude = ((((value & 0x0F) << 3) + Bias) << exponent) - Bias;

                table[i] = (short)((value & 0x80) != 0 ? -magnitude : magnitude);
            }

            return table;
        }
    }
}
