using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// A destination is a reference, and the string form is what a select option and a stored
    /// choice are made of, so reading one back has to give exactly what was written (D35).
    /// </summary>
    public class DestinationTests
    {
        [Fact]
        public void An_extension_destination_reads_back_as_itself()
        {
            var destination = new Destination(DestinationType.Extension, "1001");

            Assert.Equal("Extension:1001", destination.Key);
            Assert.True(Destination.TryParse(destination.Key, out var parsed));
            Assert.Equal(DestinationType.Extension, parsed.Type);
            Assert.Equal("1001", parsed.Value);
        }

        [Fact]
        public void A_voicemail_destination_reads_back_as_itself()
        {
            Assert.True(Destination.TryParse("Voicemail:1002", out var parsed));

            Assert.Equal(DestinationType.Voicemail, parsed.Type);
            Assert.Equal("1002", parsed.Value);
        }

        /// <summary>Nothing to point at, so nothing after the kind.</summary>
        [Fact]
        public void Hangup_has_no_value_and_no_colon()
        {
            Assert.Equal("Hangup", Destination.Hangup.Key);

            Assert.True(Destination.TryParse("Hangup", out var parsed));
            Assert.Equal(DestinationType.Hangup, parsed.Type);
            Assert.Equal("", parsed.Value);
        }

        /// <summary>The static is a fresh one each time: nobody can edit everyone else's Hangup.</summary>
        [Fact]
        public void Hangup_hands_out_its_own_instance()
        {
            var first = Destination.Hangup;
            first.Value = "1001";

            Assert.Equal("", Destination.Hangup.Value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("RingGroup:1")]        // a kind this version does not have
        [InlineData("extension:1001")]     // stored by name, and the name has a capital E
        [InlineData("Extension")]          // an extension with nothing to point at
        [InlineData("Extension:")]
        [InlineData("Extension:abc")]
        [InlineData("Extension:1")]        // too short to be an extension number
        [InlineData("Extension:1234567")]  // too long
        [InlineData("Voicemail:")]
        [InlineData("Hangup:1001")]        // hanging up on nobody in particular
        public void A_key_that_cannot_be_trusted_is_refused(string? key)
        {
            Assert.False(Destination.TryParse(key, out _));
        }

        [Fact]
        public void A_destination_that_points_at_nothing_does_not_validate()
        {
            Assert.NotEmpty(new Destination(DestinationType.Extension).Validate());
            Assert.NotEmpty(new Destination(DestinationType.Voicemail, "no").Validate());
            Assert.NotEmpty(new Destination(DestinationType.Hangup, "1001").Validate());
        }

        [Fact]
        public void A_destination_that_points_at_something_does()
        {
            Assert.Empty(new Destination(DestinationType.Extension, "1001").Validate());
            Assert.Empty(new Destination(DestinationType.Voicemail, "99").Validate());
            Assert.Empty(Destination.Hangup.Validate());
        }

        /// <summary>
        /// A number that is fine as an extension is fine as a destination, because both ask the
        /// same question of the same rule.
        /// </summary>
        [Theory]
        [InlineData("12", true)]
        [InlineData("123456", true)]
        [InlineData("1", false)]
        [InlineData("1234567", false)]
        [InlineData("10a1", false)]
        public void Destinations_use_the_extension_number_rule(string number, bool valid)
        {
            Assert.Equal(valid, Extension.IsValidNumber(number));
            Assert.Equal(valid, new Destination(DestinationType.Extension, number).Validate().Count == 0);
        }
    }
}
