using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// One voice the saved ElevenLabs key can use, as the AI settings page offers it (D205). Every
    /// field but <see cref="Id"/> is the provider's own text, so a page renders it as text, never
    /// as HTML.
    /// </summary>
    public sealed partial record AiVoice
    {
        /// <summary>ElevenLabs' grouping: premade, cloned, generated or professional.</summary>
        public string Category { get; init; } = "";

        public string Id { get; init; } = "";

        /// <summary>The voice's labels in one short line, e.g. "en · female · American", or blank.</summary>
        public string Labels { get; init; } = "";

        public string Name { get; init; } = "";

        /// <summary>
        /// Whether a chosen voice ID is shaped like one ElevenLabs issues: letters and digits only,
        /// 10 to 64 of them. Stricter than the Settings row's own check, because the picker only
        /// ever posts an ID the provider gave it, so anything else did not come from the list.
        /// </summary>
        public static bool IsPlausibleId(string value) => IdPattern().IsMatch(value);

        [GeneratedRegex(@"^[A-Za-z0-9]{10,64}\z")]
        private static partial Regex IdPattern();
    }
}
