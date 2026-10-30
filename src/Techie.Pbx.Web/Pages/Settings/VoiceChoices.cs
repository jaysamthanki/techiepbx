using Techie.Pbx.Core.Ai;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// What the AI page's "Load voices" button shows (D205): the voices the saved ElevenLabs key may
    /// use, or why there are none. The names and labels are the provider's text and are rendered as
    /// text.
    /// </summary>
    public class VoiceChoices
    {
        /// <summary>The voice ID stored now, selected in the list when it is one of them.</summary>
        public string Current { get; set; } = "";

        /// <summary>Why the list could not be loaded, as a sentence; blank when it was.</summary>
        public string Error { get; set; } = "";

        public List<AiVoice> Voices { get; set; } = new();
    }
}
