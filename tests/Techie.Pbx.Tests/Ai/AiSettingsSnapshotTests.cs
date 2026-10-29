using Techie.Pbx.Core.Ai;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Ai
{
    /// <summary>The <c>Ai.*</c> settings as a call reads them (D198, D200): blank or unreadable is the default.</summary>
    public class AiSettingsSnapshotTests
    {
        private const string Key = "test_key_0123456789abcdef";

        [Fact]
        public void Nothing_stored_is_every_default()
        {
            var snapshot = AiSettingsSnapshot.From(new Dictionary<string, string>());

            Assert.True(snapshot.BargeIn);
            Assert.True(snapshot.LogTranscripts);
            Assert.Equal(AiSettings.DefaultTtsModel, snapshot.ElevenLabsModel);
            Assert.Equal(AiSettings.DefaultSttModel, snapshot.SttModel);
            Assert.Equal(AiSettings.DefaultMediaPort, snapshot.MediaPort);
            Assert.False(snapshot.IsConfigured);
        }

        [Fact]
        public void Stored_values_are_read_and_trimmed()
        {
            var snapshot = AiSettingsSnapshot.From(new Dictionary<string, string>
            {
                [SettingsKeys.AiBargeIn] = Toggles.Off,
                [SettingsKeys.AiElevenLabsApiKey] = Key,
                [SettingsKeys.AiLogTranscripts] = Toggles.Off,
                [SettingsKeys.AiElevenLabsVoiceId] = " voice1 ",
                [SettingsKeys.AiMediaPort] = "9100",
                [SettingsKeys.AiOpenRouterApiKey] = Key,
                [SettingsKeys.AiOpenRouterModel] = "vendor/model",
            });

            Assert.False(snapshot.BargeIn);
            Assert.False(snapshot.LogTranscripts);
            Assert.Equal("voice1", snapshot.ElevenLabsVoiceId);
            Assert.Equal(9100, snapshot.MediaPort);
            Assert.Equal("vendor/model", snapshot.OpenRouterModel);
            Assert.True(snapshot.IsConfigured);
        }

        [Theory]
        [InlineData("")]
        [InlineData("80")]
        [InlineData("70000")]
        [InlineData("nine")]
        public void A_port_the_settings_page_would_refuse_is_the_default(string port)
        {
            var snapshot = AiSettingsSnapshot.From(new Dictionary<string, string> { [SettingsKeys.AiMediaPort] = port });

            Assert.Equal(AiSettings.DefaultMediaPort, snapshot.MediaPort);
        }

        [Fact]
        public void Printing_a_snapshot_never_prints_a_key()
        {
            var snapshot = new AiSettingsSnapshot { ElevenLabsApiKey = Key, OpenRouterApiKey = Key };

            Assert.DoesNotContain(Key, snapshot.ToString());
        }
    }
}
