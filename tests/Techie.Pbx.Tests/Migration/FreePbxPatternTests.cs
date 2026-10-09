using Techie.Pbx.Core.Migration;

namespace Techie.Pbx.Tests.Migration
{
    /// <summary>FreePBX's dial-pattern grammar into ours (D170): what carries, what is refused and why.</summary>
    public class FreePbxPatternTests
    {
        [Theory]
        [InlineData("NXXNXXXXXX", "_NXXNXXXXXX")]
        [InlineData("1NXXNXXXXXX", "_1NXXNXXXXXX")]
        [InlineData("_1NXXNXXXXXX", "_1NXXNXXXXXX")]
        [InlineData("NXXXXXX", "_NXXXXXX")]
        [InlineData("1800NXXXXXX", "_1800NXXXXXX")]
        [InlineData("ZXX", "_ZXX")]
        [InlineData("nxxnxxxxxx", "_NXXNXXXXXX")]
        [InlineData("[2-9]XXXXXX", "_[2-9]XXXXXX")]
        [InlineData("[123]XX", "_[123]XX")]
        [InlineData("1[2-46-9]XX", "_1[2-46-9]XX")]
        [InlineData("1.", "_1.")]
        [InlineData("1800.", "_1800.")]
        [InlineData("NXX-XXXX", "_NXXXXXX")]
        [InlineData("  NXXXXXX  ", "_NXXXXXX")]
        public void Translates(string freePbx, string expected)
        {
            Assert.True(FreePbxPattern.TryTranslate(freePbx, out var pattern, out var strip, out var problem), problem);
            Assert.Equal(expected, pattern);
            Assert.Equal(0, strip);
        }

        [Theory]
        [InlineData("911")]
        [InlineData("211")]
        [InlineData("933")]
        public void A_plain_number_is_an_exact_match(string number)
        {
            Assert.True(FreePbxPattern.TryTranslate(number, out var pattern, out _, out _));
            Assert.Equal("_" + number, pattern);
        }

        [Fact]
        public void The_legacy_prefix_form_becomes_the_whole_number_with_a_strip()
        {
            Assert.True(FreePbxPattern.TryTranslate("9|NXXXXXX", out var pattern, out var strip, out _));
            Assert.Equal("_9NXXXXXX", pattern);
            Assert.Equal(1, strip);

            Assert.True(FreePbxPattern.TryTranslate("81|1NXXNXXXXXX", out pattern, out strip, out _));
            Assert.Equal("_811NXXNXXXXXX", pattern);
            Assert.Equal(2, strip);
        }

        [Theory]
        [InlineData("011.")]
        [InlineData("00.")]
        [InlineData("X.")]
        [InlineData("XXXXXXX")]
        [InlineData("[0-9]XX")]
        [InlineData(".")]
        public void International_reach_is_refused_as_on_the_form(string freePbx)
        {
            Assert.False(FreePbxPattern.TryTranslate(freePbx, out var pattern, out _, out var problem));
            Assert.Equal("", pattern);
            Assert.NotEqual("", problem);
        }

        [Theory]
        [InlineData("", "empty")]
        [InlineData(null, "empty")]
        [InlineData("_", "empty")]
        [InlineData("1!", "'!'")]
        [InlineData("*98", "'*'")]
        [InlineData("#21", "'#'")]
        [InlineData("+1NXX", "'+'")]
        [InlineData("1N.X", "last character")]
        [InlineData("1[2-", "matching ]")]
        [InlineData("N|XXXX", "prefix")]
        public void Anything_else_is_refused_with_a_reason(string? freePbx, string reason)
        {
            Assert.False(FreePbxPattern.TryTranslate(freePbx, out _, out var strip, out var problem));
            Assert.Contains(reason, problem);
            Assert.Equal(0, strip);
        }
    }
}
