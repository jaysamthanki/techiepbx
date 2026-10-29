using Techie.Pbx.Core.Ai;

namespace Techie.Pbx.Tests.Ai
{
    /// <summary>When a caller has finished speaking (D200): energy, 300 ms of speech, 400 ms of quiet, 20 s cap.</summary>
    public class EndpointerTests
    {
        [Fact]
        public void Speech_then_400_ms_of_silence_completes_a_turn()
        {
            var endpointer = new Endpointer();

            Assert.False(endpointer.Add(TestAudio.Speech(600)));
            Assert.False(endpointer.Add(TestAudio.Silence(Endpointer.TrailingSilenceMs - 20)));
            Assert.True(endpointer.Add(TestAudio.Silence(20)));
            Assert.Equal((600 + Endpointer.TrailingSilenceMs) * 8, endpointer.Take().Length);
        }

        [Fact]
        public void Pure_silence_never_completes()
        {
            var endpointer = new Endpointer();

            Assert.False(endpointer.Add(TestAudio.Silence(30_000, level: 100)));
            Assert.False(endpointer.IsSpeaking);
        }

        [Fact]
        public void A_burst_shorter_than_the_minimum_is_forgotten()
        {
            var endpointer = new Endpointer();

            Assert.False(endpointer.Add(TestAudio.Speech(100).Then(TestAudio.Silence(1000))));
            Assert.False(endpointer.IsSpeaking);

            Assert.True(endpointer.Add(TestAudio.Speech(400).Then(TestAudio.Silence(400))));
        }

        [Fact]
        public void A_long_utterance_is_cut_at_the_cap()
        {
            var endpointer = new Endpointer();

            Assert.True(endpointer.Add(TestAudio.Speech(25_000)));
            Assert.Equal(Endpointer.MaxUtteranceMs * 8, endpointer.Take().Length);
        }

        [Fact]
        public void The_pre_roll_comes_back_with_the_utterance()
        {
            var endpointer = new Endpointer();

            endpointer.Add(TestAudio.Silence(1000, level: 7));
            Assert.True(endpointer.Add(TestAudio.Speech(500).Then(TestAudio.Silence(400))));

            var utterance = endpointer.Take();

            Assert.Equal((Endpointer.PreRollMs + 500 + 400) * 8, utterance.Length);
            Assert.All(utterance[..(Endpointer.PreRollMs * 8)], s => Assert.Equal(7, s));
        }

        [Fact]
        public void Take_starts_the_next_turn_afresh()
        {
            var endpointer = new Endpointer();

            endpointer.Add(TestAudio.Speech(400).Then(TestAudio.Silence(400)));
            endpointer.Take();

            Assert.False(endpointer.IsComplete);
            Assert.False(endpointer.Add(TestAudio.Silence(1000)));
        }

        [Fact]
        public void Samples_need_not_arrive_in_whole_windows()
        {
            var endpointer = new Endpointer();
            var audio = TestAudio.Speech(500).Then(TestAudio.Silence(400));
            var complete = false;

            for (var offset = 0; offset < audio.Length; offset += 37)
                complete = endpointer.Add(audio.AsSpan(offset, Math.Min(37, audio.Length - offset)));

            Assert.True(complete);
        }
    }
}
