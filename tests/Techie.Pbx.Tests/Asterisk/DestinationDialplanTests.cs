using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The one place that knows how to send a call somewhere (D36). Every feature that ends in
    /// "and then the call goes here" writes exactly these lines, so they are worth pinning down.
    /// </summary>
    public class DestinationDialplanTests
    {
        [Fact]
        public void An_extension_destination_goes_in_by_the_front_door()
        {
            var steps = DestinationDialplan.Steps(new Destination(DestinationType.Extension, "1001"));

            // The same entry an internal call uses, so the voicemail fallback happens for an
            // inbound call too, without anyone writing it twice.
            Assert.Equal(new[] { $"Goto({ExtensionsConfRenderer.InternalContext},1001,1)" }, steps);
        }

        [Fact]
        public void A_voicemail_destination_takes_a_message_and_hangs_up()
        {
            var steps = DestinationDialplan.Steps(new Destination(DestinationType.Voicemail, "1001"));

            Assert.Equal(
                new[] { $"VoiceMail(1001@{VoicemailConfRenderer.MailboxContext},u)", "Hangup()" },
                steps);
        }

        [Fact]
        public void The_busy_greeting_is_asked_for_by_name()
        {
            var steps = DestinationDialplan.Steps(new Destination(DestinationType.Voicemail, "1001"), VoicemailGreeting.Busy);

            Assert.Equal($"VoiceMail(1001@{VoicemailConfRenderer.MailboxContext},b)", steps[0]);
        }

        [Fact]
        public void Hangup_hangs_up()
        {
            Assert.Equal(new[] { "Hangup()" }, DestinationDialplan.Steps(Destination.Hangup));
        }

        [Fact]
        public void Lines_are_ready_to_write_into_a_dialplan()
        {
            Assert.Equal(
                " same => n,Goto(internal,1001,1)\n",
                DestinationDialplan.Lines(new Destination(DestinationType.Extension, "1001")));
        }

        /// <summary>A label goes on the first line only: the rest just carry on.</summary>
        [Fact]
        public void A_label_marks_the_first_line_so_a_goto_can_reach_it()
        {
            var lines = DestinationDialplan.Lines(new Destination(DestinationType.Voicemail, "1001"), "busy", VoicemailGreeting.Busy);

            Assert.Equal(
                " same => n(busy),VoiceMail(1001@default,b)\n same => n,Hangup()\n",
                lines);
        }

        /// <summary>
        /// The same lines the extensions renderer writes for its own fallback, which is the point:
        /// one syntax, one place (D36).
        /// </summary>
        [Fact]
        public void The_extension_fallback_is_written_by_this_helper()
        {
            var extensions = new List<Extension>
            {
                new()
                {
                    Number = "1001",
                    Name = "Front Desk",
                    Secret = "AAAAbbbbCCCCdddd1111",
                    VoicemailEnabled = true,
                    VoicemailPin = "4321",
                },
            };

            var dialplan = ExtensionsConfRenderer.Render(extensions);
            var mailbox = new Destination(DestinationType.Voicemail, "1001");

            Assert.Contains(DestinationDialplan.Lines(mailbox, "busy", VoicemailGreeting.Busy), dialplan);
            Assert.Contains(DestinationDialplan.Lines(mailbox, "unavailable"), dialplan);
        }

        [Fact]
        public void A_destination_that_does_not_validate_is_never_written()
        {
            Assert.Throws<InvalidOperationException>(() =>
                DestinationDialplan.Steps(new Destination(DestinationType.Extension, "")));

            Assert.Throws<InvalidOperationException>(() =>
                DestinationDialplan.Steps(new Destination(DestinationType.Voicemail, "not-a-number")));
        }

        /// <summary>
        /// Validation would have caught these, and the renderer still refuses them: a value that
        /// reached the database another way cannot start a new dialplan line.
        /// </summary>
        [Theory]
        [InlineData("1001)\nexten => _X.,1,Dial(SIP/evil")]
        [InlineData("1001; comment")]
        [InlineData("1001]")]
        public void Injection_is_refused_even_if_validation_was_bypassed(string value)
        {
            var destination = new Destination(DestinationType.Extension, value);

            Assert.Throws<InvalidOperationException>(() => DestinationDialplan.Steps(destination));
        }

        [Fact]
        public void A_label_is_checked_like_everything_else_that_reaches_a_conf_file()
        {
            Assert.Throws<InvalidOperationException>(() =>
                DestinationDialplan.Lines(Destination.Hangup, "busy)\nexten => 999,1,NoOp("));
        }
    }
}
