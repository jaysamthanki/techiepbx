using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The two words a yes/no setting may hold, and what a missing one means. The interesting part
    /// is the third state: blank is "not set" everywhere in the Settings table, and it has to mean
    /// the default the reading code names rather than off, or clearing a box would quietly turn a
    /// feature off instead of putting it back where it started.
    /// </summary>
    public class TogglesTests
    {
        [Fact]
        public void On_is_offered_first_because_the_form_treats_the_first_choice_as_the_default()
        {
            Assert.Equal(new[] { Toggles.On, Toggles.Off }, Toggles.All);
        }

        [Theory]
        [InlineData("on", true)]
        [InlineData("off", true)]
        [InlineData("On", false)]
        [InlineData("true", false)]
        [InlineData("yes", false)]
        [InlineData("", false)]
        public void Only_the_two_exact_words_may_be_stored(string value, bool expected)
        {
            Assert.Equal(expected, Toggles.IsKnown(value));
        }

        [Theory]
        [InlineData("on", true)]
        [InlineData("  on  ", true)]
        [InlineData("off", false)]
        [InlineData("anything else", false)]
        public void Anything_that_is_not_on_is_off(string value, bool expected)
        {
            Assert.Equal(expected, Toggles.IsOn(value));
        }

        [Fact]
        public void A_key_nobody_stored_is_whatever_the_caller_says_the_default_is()
        {
            var settings = new Dictionary<string, string>();

            Assert.True(Toggles.Is(settings, "Some.Key", whenUnset: true));
            Assert.False(Toggles.Is(settings, "Some.Key", whenUnset: false));
        }

        /// <summary>
        /// Clearing a setting is how an admin puts it back to the default, so a blank value has to
        /// behave exactly like a row that was never written.
        /// </summary>
        [Fact]
        public void A_blank_value_is_the_default_and_not_off()
        {
            var settings = new Dictionary<string, string> { ["Some.Key"] = "   " };

            Assert.True(Toggles.Is(settings, "Some.Key", whenUnset: true));
        }

        [Fact]
        public void A_stored_value_wins_over_the_default_either_way()
        {
            Assert.False(Toggles.Is(new Dictionary<string, string> { ["Some.Key"] = Toggles.Off }, "Some.Key", whenUnset: true));
            Assert.True(Toggles.Is(new Dictionary<string, string> { ["Some.Key"] = Toggles.On }, "Some.Key", whenUnset: false));
        }
    }
}
