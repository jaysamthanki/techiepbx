namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// The defaults behind the <c>Ai.*</c> settings (F11, D198). Named here rather than typed into
    /// the settings page so the page cannot claim a default the code that reads the setting does
    /// not use — the same rule the AMI, PJSIP and Asterisk settings already follow (D67).
    ///
    /// The TTS model is <b>Flash v2.5</b> on purpose: it is the one ElevenLabs model tuned for
    /// telephony latency (roughly 75 ms), and a phone call has no patience for the high-quality
    /// models' slower first byte.
    /// </summary>
    public static class AiSettings
    {
        /// <summary>The lowest port the media listener may use: below this a binding needs privilege the app does not have.</summary>
        public const int MinMediaPort = 1024;

        /// <summary>The port Asterisk's AudioSocket() dials the media listener on, on loopback only.</summary>
        public const int DefaultMediaPort = 9019;

        /// <summary>The ElevenLabs speech-to-text model. Blank asks the provider for its default.</summary>
        public const string DefaultSttModel = "scribe_v1";

        /// <summary>The ElevenLabs text-to-speech model: the low-latency one, because this audio is a live call.</summary>
        public const string DefaultTtsModel = "eleven_flash_v2_5";
    }
}
