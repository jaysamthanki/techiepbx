using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// What the provisioning endpoint will and will not accept as "a Polycom phone is asking"
    /// (D78). The headers here are the real shapes: a VVX, an older SoundStation, and a PolyEdge.
    /// </summary>
    public class PolycomUserAgentTests
    {
        [Theory]
        [InlineData("FileTransport PolycomVVX-VVX_410-UA/5.9.5.0614 Type/Application", "VVX_410", "5.9.5.0614")]
        [InlineData("FileTransport PolycomVVX-VVX_501-UA/6.4.6.0123", "VVX_501", "6.4.6.0123")]
        [InlineData("FileTransport PolycomSoundStationIP-SSIP_5000-UA/4.0.9.1400", "SSIP_5000", "4.0.9.1400")]
        [InlineData("FileTransport PolycomSoundPointIP-SPIP_450-UA/3.2.3.1734", "SPIP_450", "3.2.3.1734")]
        [InlineData("FileTransport PolyEdge-Edge_E350-UA/8.0.0.1234", "Edge_E350", "8.0.0.1234")]
        public void A_phone_is_recognised_with_its_model_and_firmware(string header, string model, string firmware)
        {
            Assert.True(PolycomUserAgent.TryParse(header, out var agent));
            Assert.Equal(model, agent.Model);
            Assert.Equal(firmware, agent.Firmware);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36")]
        [InlineData("curl/8.5.0")]
        [InlineData("FileTransport")]
        [InlineData("FileTransport Yealink-T46S-UA/66.85.0.5")]
        [InlineData("PolycomVVX-VVX_410-UA/5.9.5.0614")]
        [InlineData("FileTransport PolycomVVX-VVX_410-UA/")]
        [InlineData("FileTransport PolycomVVX-VVX_410-UA/x.y.z")]
        public void Anything_else_is_not_a_phone(string? header)
        {
            Assert.False(PolycomUserAgent.TryParse(header, out _));
        }

        /// <summary>
        /// The header is anchored at the start, so a Polycom-looking tail on somebody else's
        /// header does not get in.
        /// </summary>
        [Fact]
        public void A_polycom_header_buried_in_another_one_is_not_a_phone()
        {
            Assert.False(PolycomUserAgent.TryParse(
                "curl/8.5.0 FileTransport PolycomVVX-VVX_410-UA/5.9.5.0614", out _));
        }

        /// <summary>A header long enough to be an attack rather than a phone is refused outright.</summary>
        [Fact]
        public void An_absurdly_long_header_is_refused()
        {
            var header = "FileTransport PolycomVVX-VVX_410-UA/5.9.5.0614 " + new string('x', 300);

            Assert.False(PolycomUserAgent.TryParse(header, out _));
        }
    }
}
