using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// How an astdb value becomes the badge the call flow controls page shows (F9). Reading it
    /// over a real connection is checked on the lab VM, and the DBGet exchange itself in
    /// <see cref="AmiSessionTests"/>; this is the part that decides what the admin sees.
    /// </summary>
    public class CallFlowControlStatusTests
    {
        /// <summary>
        /// The same rule the dialplan uses: exactly 1 is on. A switch nobody has flipped has no
        /// key at all, which is off, not unknown.
        /// </summary>
        [Theory]
        [InlineData(CallFlowControl.StateOn, CallFlowState.Override)]
        [InlineData(CallFlowControl.StateOff, CallFlowState.Normal)]
        [InlineData("", CallFlowState.Normal)]
        [InlineData(null, CallFlowState.Normal)]
        [InlineData(" 1", CallFlowState.Normal)]
        [InlineData("yes", CallFlowState.Normal)]
        public void Only_exactly_on_is_the_override(string? value, CallFlowState expected)
        {
            Assert.Equal(expected, CallFlowControlStatus.Map(value));
        }

        [Fact]
        public void No_switches_is_no_answers_and_no_connection()
        {
            // Nothing is listening on this port; with nothing to ask, it is never tried.
            var ami = new AmiSettings { Port = 1, Username = "tnpbx", Secret = "not-a-real-secret" };

            Assert.Empty(CallFlowControlStatus.Read(ami, new List<CallFlowControl>()));
        }

        /// <summary>
        /// An Asterisk that cannot be reached makes every switch Unknown rather than failing the
        /// page, and never guesses Normal: that would be a badge saying the day mode is on when
        /// nobody knows.
        /// </summary>
        [Fact]
        public void An_unreachable_asterisk_makes_every_switch_unknown()
        {
            var ami = new AmiSettings { Port = 1, Username = "tnpbx", Secret = "not-a-real-secret", TimeoutSeconds = 2 };
            var controls = new List<CallFlowControl>
            {
                new() { CallFlowControlID = 1, Name = "Night mode", FeatureCode = "*28" },
                new() { CallFlowControlID = 2, Name = "Lunch", FeatureCode = "*29" },
            };

            var states = CallFlowControlStatus.Read(ami, controls);

            Assert.Equal(new long[] { 1, 2 }, states.Keys.OrderBy(k => k));
            Assert.All(states.Values, s => Assert.Equal(CallFlowState.Unknown, s));
        }
    }
}
