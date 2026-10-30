using System.Text;
using Techie.Pbx.Core.Ai;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Web.Pages.Settings;

namespace Techie.Pbx.Tests.Ai
{
    /// <summary>
    /// The voice chooser on the AI settings page (D205), without a network: the v1 voice list read,
    /// a refusal turned into a sentence, and the shape a chosen voice ID must have.
    /// </summary>
    public class AiVoicesTests
    {
        private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

        [Fact]
        public void Two_voices_are_read_and_ordered_by_category_then_name()
        {
            var voices = AiProviders.ReadVoices(Bytes(
                """
                {"voices":[
                  {"voice_id":"21m00Tcm4TlvDq8ikWAM","name":"Rachel","category":"premade",
                   "labels":{"accent":"American","gender":"female","age":"young","language":"en","use_case":"narration"}},
                  {"voice_id":"Zx9Yw8Vu7Ts6Rq5Po4Nm","name":"Front desk","category":"cloned"}
                ]}
                """));

            Assert.Equal(2, voices.Count);

            Assert.Equal("cloned", voices[0].Category);
            Assert.Equal("Zx9Yw8Vu7Ts6Rq5Po4Nm", voices[0].Id);
            Assert.Equal("Front desk", voices[0].Name);
            Assert.Equal("", voices[0].Labels);

            Assert.Equal("premade", voices[1].Category);
            Assert.Equal("21m00Tcm4TlvDq8ikWAM", voices[1].Id);
            Assert.Equal("Rachel", voices[1].Name);
            Assert.Equal("en · female · young · American", voices[1].Labels);
        }

        [Fact]
        public void Within_a_category_voices_are_ordered_by_name()
        {
            var voices = AiProviders.ReadVoices(Bytes(
                """{"voices":[{"voice_id":"BBBBBBBBBBBB","name":"Zoe","category":"premade"},{"voice_id":"AAAAAAAAAAAA","name":"adam","category":"premade"}]}"""));

            Assert.Equal(new[] { "adam", "Zoe" }, voices.Select(v => v.Name));
        }

        [Fact]
        public void An_empty_list_is_no_voices()
        {
            Assert.Empty(AiProviders.ReadVoices(Bytes("""{"voices":[]}""")));
        }

        [Fact]
        public void A_voice_whose_id_could_not_be_saved_is_left_out()
        {
            var voices = AiProviders.ReadVoices(Bytes(
                """{"voices":[{"voice_id":"<script>","name":"Bad"},{"name":"No id"},{"voice_id":"AAAAAAAAAAAA","name":"Good"}]}"""));

            Assert.Equal("Good", Assert.Single(voices).Name);
        }

        [Fact]
        public void Something_other_than_the_voice_list_is_a_provider_failure()
        {
            Assert.Throws<AiProviderException>(() => AiProviders.ReadVoices(Bytes("""{"detail":"nope"}""")));
            Assert.Throws<AiProviderException>(() => AiProviders.ReadVoices(Bytes("not json")));
        }

        [Fact]
        public void A_key_without_voices_read_is_told_what_permission_it_needs()
        {
            var message = AiProviders.VoicesError(401, "Unauthorized", Bytes(
                """{"detail":{"status":"missing_permissions","message":"The API key you used is missing the permission voices_read to execute this operation."}}"""), "");

            Assert.Equal("The ElevenLabs key cannot read voices: it needs the voices_read permission. Add it to the key, or type the voice ID by hand.", message);
        }

        [Fact]
        public void A_402_keeps_the_providers_own_reason()
        {
            var message = AiProviders.VoicesError(402, "Payment Required", Bytes(
                """{"detail":{"status":"payment_required","message":"Free users cannot use library voices via the API."}}"""), "");

            Assert.Equal("ElevenLabs refused the request: Free users cannot use library voices via the API.", message);
        }

        [Fact]
        public void Any_other_refusal_keeps_the_status_and_the_text()
        {
            var unauthorised = AiProviders.VoicesError(401, "Unauthorized", Bytes("""{"detail":{"status":"invalid_api_key"}}"""), "");
            var broken = AiProviders.VoicesError(500, "Internal Server Error", Bytes("upstream fell over"), "");

            Assert.Equal("""ElevenLabs voices answered 401 Unauthorized: {"detail":{"status":"invalid_api_key"}}""", unauthorised);
            Assert.Equal("ElevenLabs voices answered 500 Internal Server Error: upstream fell over", broken);
        }

        [Fact]
        public void A_refusal_never_carries_the_key()
        {
            var message = AiProviders.VoicesError(402, "Payment Required", Bytes("""{"detail":{"message":"not for test-key-value"}}"""), "test-key-value");

            Assert.DoesNotContain("test-key-value", message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("21m00Tcm4TlvDq8ikWAM", true)]
        [InlineData("AAAAAAAAAA", true)]
        [InlineData("<script>", false)]
        [InlineData("<script>alert(1)</script>", false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("short", false)]
        [InlineData("with-dash-0123", false)]
        public void A_chosen_voice_id_must_look_like_one(string value, bool plausible)
        {
            Assert.Equal(plausible, AiVoice.IsPlausibleId(value));
        }

        [Fact]
        public void A_two_hundred_character_voice_id_is_refused()
        {
            Assert.False(AiVoice.IsPlausibleId(new string('a', 200)));
        }

        [Fact]
        public void The_chooser_sits_above_the_voice_id_row()
        {
            var speech = AiSettingsModel.Sections(new Dictionary<string, string>()).Single(s => s.Title == "Speech");

            Assert.Equal(SettingsKeys.AiElevenLabsVoiceId, speech.InsertBeforeKey);
            Assert.Contains(speech.Rows, r => r.Key == speech.InsertBeforeKey);
            Assert.EndsWith("_VoiceChooser.cshtml", speech.InsertPartial, StringComparison.Ordinal);
        }
    }
}
