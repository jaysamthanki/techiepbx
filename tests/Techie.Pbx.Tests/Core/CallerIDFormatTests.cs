using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// The two forms a caller ID may be typed in, and what is deliberately not one of them (D125).
    /// One reader for both the extension's own caller ID and an outbound route's, because both end up
    /// in the same <c>CALLERID(all)</c>.
    /// </summary>
    public class CallerIDFormatTests
    {
        [Fact]
        public void A_bare_number_is_a_caller_id_with_no_name()
        {
            Assert.True(CallerIDFormat.TryParse("17141234567", out var name, out var number));
            Assert.Equal("", name);
            Assert.Equal("17141234567", number);
        }

        [Fact]
        public void A_name_and_number_come_back_as_two_parts()
        {
            Assert.True(CallerIDFormat.TryParse("\"Acme Sales\" <17141234567>", out var name, out var number));
            Assert.Equal("Acme Sales", name);
            Assert.Equal("17141234567", number);
        }

        /// <summary>
        /// The quotes are added on the way out, so an admin who leaves them off means the same thing.
        /// Spaces around the pieces are theirs to be careless with.
        /// </summary>
        [Theory]
        [InlineData("Acme Sales <17141234567>")]
        [InlineData("  \"Acme Sales\"  <17141234567>  ")]
        [InlineData("\"Acme Sales\"<17141234567>")]
        public void The_quotes_and_the_spacing_are_optional(string value)
        {
            Assert.True(CallerIDFormat.TryParse(value, out var name, out var number));
            Assert.Equal("Acme Sales", name);
            Assert.Equal("17141234567", number);
        }

        [Fact]
        public void A_number_in_brackets_with_no_name_is_still_a_number()
        {
            Assert.True(CallerIDFormat.TryParse("<17141234567>", out var name, out var number));
            Assert.Equal("", name);
            Assert.Equal("17141234567", number);
        }

        /// <summary>
        /// Deliberately narrower than Asterisk: a number is digits, and a name carries nothing that
        /// would end a dialplan argument early or open a section in a conf file.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("+17141234567")]
        [InlineData("1-714-123-4567")]
        [InlineData("Acme Sales")]
        [InlineData("\"Acme, Sales\" <17141234567>")]
        [InlineData("\"Acme (Sales)\" <17141234567>")]
        [InlineData("\"Acme\" <7141234>; comment")]
        [InlineData("\"Acme\" <>")]
        [InlineData("\"Acme\" <7141234567890123456>")]
        [InlineData("\"This name is far too long to be a caller ID name\" <100>")]
        public void What_is_not_a_caller_id(string value)
        {
            Assert.False(CallerIDFormat.TryParse(value, out _, out _));
        }

        /// <summary>Empty is not a caller ID, and is not an error either: it means "name nothing".</summary>
        [Fact]
        public void Empty_is_allowed_and_means_none()
        {
            Assert.Null(CallerIDFormat.Error("", "Caller ID"));
            Assert.Null(CallerIDFormat.Error("   ", "Caller ID"));
        }

        [Fact]
        public void The_message_names_the_field_it_is_about()
        {
            Assert.StartsWith("Outbound caller ID", CallerIDFormat.Error("nonsense", "Outbound caller ID"));
            Assert.StartsWith("Caller ID", CallerIDFormat.Error("nonsense", "Caller ID"));
        }
    }
}
