using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// What the Yealink provisioning endpoint will and will not accept as "a Yealink phone is
    /// asking" (D88, mirroring D78). The headers here are the real shape: "Yealink", a model as
    /// "SIP-" plus the part we store, firmware, then the phone's own MAC.
    /// </summary>
    public class YealinkUserAgentTests
    {
        [Theory]
        [InlineData("Yealink SIP-T33G 124.86.0.118 24:9a:d8:1e:83:fa", "T33G", "124.86.0.118", "249ad81e83fa")]
        [InlineData("Yealink SIP-T46S 66.85.0.5 00:15:65:aa:bb:cc", "T46S", "66.85.0.5", "001565aabbcc")]
        [InlineData("Yealink SIP-T54W 96.85.0.15 80:5e:c0:11:22:33", "T54W", "96.85.0.15", "805ec0112233")]
        public void A_phone_is_recognised_with_its_model_firmware_and_mac(
            string header, string model, string firmware, string mac)
        {
            Assert.True(YealinkUserAgent.TryParse(header, out var agent));
            Assert.Equal(model, agent.Model);
            Assert.Equal(firmware, agent.Firmware);
            Assert.Equal(mac, agent.Mac);
        }

        /// <summary>An upper case MAC in the header is still normalised to lower case, no colons.</summary>
        [Fact]
        public void The_mac_is_normalised_regardless_of_case()
        {
            Assert.True(YealinkUserAgent.TryParse("Yealink SIP-T33G 124.86.0.118 24:9A:D8:1E:83:FA", out var agent));
            Assert.Equal("249ad81e83fa", agent.Mac);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36")]
        [InlineData("curl/8.5.0")]
        [InlineData("Yealink")]
        [InlineData("FileTransport PolycomVVX-VVX_410-UA/5.9.5.0614 Type/Application")]
        [InlineData("SIP-T33G 124.86.0.118 24:9a:d8:1e:83:fa")]
        [InlineData("Yealink SIP-T33G 124.86.0.118")]
        [InlineData("Yealink SIP-T33G 124.86.0.118 24:9a:d8:1e:83")]
        [InlineData("Yealink T33G 124.86.0.118 24:9a:d8:1e:83:fa")]
        public void Anything_else_is_not_a_phone(string? header)
        {
            Assert.False(YealinkUserAgent.TryParse(header, out _));
        }

        /// <summary>
        /// The header is anchored at the start, so a Yealink-looking tail on somebody else's
        /// header does not get in.
        /// </summary>
        [Fact]
        public void A_yealink_header_buried_in_another_one_is_not_a_phone()
        {
            Assert.False(YealinkUserAgent.TryParse(
                "curl/8.5.0 Yealink SIP-T33G 124.86.0.118 24:9a:d8:1e:83:fa", out _));
        }

        /// <summary>A header long enough to be an attack rather than a phone is refused outright.</summary>
        [Fact]
        public void An_absurdly_long_header_is_refused()
        {
            var header = "Yealink SIP-T33G 124.86.0.118 24:9a:d8:1e:83:fa " + new string('x', 300);

            Assert.False(YealinkUserAgent.TryParse(header, out _));
        }
    }
}
