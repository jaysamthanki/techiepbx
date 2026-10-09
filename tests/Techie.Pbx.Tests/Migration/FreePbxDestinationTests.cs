using Techie.Pbx.Core.Migration;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Migration
{
    /// <summary>FreePBX inbound destinations: two mean an extension, everything else means nothing.</summary>
    public class FreePbxDestinationTests
    {
        [Theory]
        [InlineData("ext-local,101,1", "101")]
        [InlineData("from-did-direct,151,1", "151")]
        [InlineData(" ext-local , 2001 , 1 ", "2001")]
        public void Extension_destinations_carry(string raw, string number)
        {
            var destination = FreePbxDestination.Translate(raw);

            Assert.NotNull(destination);
            Assert.Equal(DestinationType.Extension, destination!.Type);
            Assert.Equal(number, destination.Value);
        }

        [Theory]
        [InlineData("ivr-1,s,1")]
        [InlineData("ivr-12,s,1")]
        [InlineData("ext-group,600,1")]
        [InlineData("ext-queues,400,1")]
        [InlineData("timeconditions,1,1")]
        [InlineData("ext-local,vmb301,1")]
        [InlineData("ext-local,vmu301,1")]
        [InlineData("app-blackhole,hangup,1")]
        [InlineData("ext-local,101")]
        [InlineData("ext-local,101,2")]
        [InlineData("ext-local,1,1")]
        [InlineData("ext-local,1234567,1")]
        [InlineData("")]
        [InlineData(null)]
        public void Anything_else_is_unsupported(string? raw)
        {
            Assert.Null(FreePbxDestination.Translate(raw));
        }
    }
}
