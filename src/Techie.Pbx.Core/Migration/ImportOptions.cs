namespace Techie.Pbx.Core.Migration
{
    /// <summary>The choices the operator makes on the preview.</summary>
    public class ImportOptions
    {
        /// <summary>
        /// Whether each extension's voicemail email also becomes its /phone sign-in address
        /// (<see cref="Models.Extension.UserEmail"/>, D166). Off by default: the sign-in address
        /// decides who may open an extension in the browser, and FreePBX's voicemail address was
        /// never asked that question.
        /// </summary>
        public bool UserEmailFromVoicemail { get; set; }
    }
}
