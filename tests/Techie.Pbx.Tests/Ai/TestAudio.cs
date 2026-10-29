namespace Techie.Pbx.Tests.Ai
{
    /// <summary>Synthetic 8 kHz audio: a loud tone that counts as speech, and silence.</summary>
    internal static class TestAudio
    {
        public static short[] Silence(int milliseconds, short level = 0) =>
            Enumerable.Repeat(level, milliseconds * 8).ToArray();

        public static short[] Speech(int milliseconds) =>
            Enumerable.Range(0, milliseconds * 8)
                .Select(i => (short)(3000 * Math.Sin(2 * Math.PI * 440 * i / 8000.0)))
                .ToArray();

        public static short[] Then(this short[] first, short[] second) => first.Concat(second).ToArray();
    }
}
