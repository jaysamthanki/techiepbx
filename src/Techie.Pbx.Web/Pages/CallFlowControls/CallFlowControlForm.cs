using Techie.Pbx.Web.Pages.Shared;

namespace Techie.Pbx.Web.Pages.CallFlowControls
{
    /// <summary>
    /// What the create/edit form in the modal shows, and what it posts back. The text fields are
    /// nullable because model binding turns a field the user left blank into null whatever the
    /// initialiser says.
    /// </summary>
    public class CallFlowControlForm
    {
        public long CallFlowControlID { get; set; }
        public List<string> Errors { get; set; } = new();
        public string? FeatureCode { get; set; } = "";
        public bool IsNew => this.CallFlowControlID == 0;
        public string? Name { get; set; } = "";

        /// <summary>Where a call goes while the switch is off, as the picker posts it (D35).</summary>
        public string? NormalDestination { get; set; } = "";

        /// <summary>The normal picker, filled in by the page. Not posted back.</summary>
        public DestinationSelect NormalDestinationChoices { get; set; } = new();

        /// <summary>Where a call goes while the switch is on, as the picker posts it (D35).</summary>
        public string? OverrideDestination { get; set; } = "";

        /// <summary>The override picker, filled in by the page. Not posted back.</summary>
        public DestinationSelect OverrideDestinationChoices { get; set; } = new();
    }
}
