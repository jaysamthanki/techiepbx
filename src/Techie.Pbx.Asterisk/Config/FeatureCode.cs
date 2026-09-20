namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// One dialable code, in the words the person holding the handset needs (D120). Not a model
    /// and not a setting: it is derived from what the renderers generate, so there is nothing to
    /// store and nothing that can drift out of step with the conf files by being edited.
    /// </summary>
    public class FeatureCode
    {
        /// <summary>The digits themselves, e.g. <c>*97</c>, or a range like <c>1-9</c>.</summary>
        public string Code { get; set; } = "";

        /// <summary>What happens, in a sentence somebody who is not an admin can follow.</summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Which half of the sheet it belongs on: one of <see cref="FeatureCodes.DuringACall"/> or
        /// <see cref="FeatureCodes.FromYourPhone"/>. A code is useless without knowing when it
        /// works, and "during a call" versus "instead of a call" is the whole of that distinction.
        /// </summary>
        public string Group { get; set; } = "";

        /// <summary>The short name of the thing, e.g. "Echo test".</summary>
        public string Name { get; set; } = "";
    }
}
