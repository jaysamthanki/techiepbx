using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    public class ConfRendererTests
    {
        private static List<Extension> SampleExtensions() => new()
        {
            // Deliberately out of order, plus a disabled one that must not be rendered.
            new Extension { Number = "1002", Name = "O'Brien (Sales)", Secret = "EEEEffffGGGGhhhh2222" },
            new Extension { Number = "1003", Name = "Disabled Phone", Secret = "IIIIjjjjKKKKllll3333", Enabled = false },
            new Extension { Number = "1001", Name = "Front Desk", Secret = "AAAAbbbbCCCCdddd1111" },
        };

        private static PjsipTransport NatTransport() => new()
        {
            LocalNets = { "10.8.20.0/24" },
            ExternalAddress = "203.0.113.10",
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        [Fact]
        public void Pjsip_matches_expected_file()
        {
            var actual = PjsipConfRenderer.Render(NatTransport(), SampleExtensions());
            Assert.Equal(Expected("pjsip.conf"), actual);
        }

        [Fact]
        public void Extensions_matches_expected_file()
        {
            var actual = ExtensionsConfRenderer.Render(SampleExtensions());
            Assert.Equal(Expected("extensions.conf"), actual);
        }

        [Fact]
        public void Pjsip_without_nat_has_no_external_addresses()
        {
            var actual = PjsipConfRenderer.Render(new PjsipTransport(), SampleExtensions());
            Assert.DoesNotContain("external_", actual);
            Assert.DoesNotContain("local_net", actual);
        }

        [Theory]
        [InlineData("Evil\n[evil]\ntype = endpoint")]
        [InlineData("Evil; comment")]
        [InlineData("Evil\"quote")]
        public void Renderers_refuse_injection_even_if_validation_was_bypassed(string name)
        {
            // Simulates a row that got into the database without going through the repository.
            var extensions = new List<Extension> { new() { Number = "1001", Name = name, Secret = "AAAAbbbbCCCCdddd1111" } };

            Assert.Throws<InvalidOperationException>(() => PjsipConfRenderer.Render(new PjsipTransport(), extensions));
            Assert.Throws<InvalidOperationException>(() => ExtensionsConfRenderer.Render(extensions));
        }

        [Fact]
        public void Pjsip_rejects_external_address_without_local_net()
        {
            var transport = new PjsipTransport { ExternalAddress = "203.0.113.10" };
            Assert.Throws<InvalidOperationException>(() => PjsipConfRenderer.Render(transport, SampleExtensions()));
        }
    }
}
