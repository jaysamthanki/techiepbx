using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Web.Pages.TimeConditions;

namespace Techie.Pbx.Web.Pages.CallFlowControls
{
    /// <summary>One line of the call flow controls table.</summary>
    public class CallFlowControlRow
    {
        public long CallFlowControlID { get; set; }
        public string FeatureCode { get; set; } = "";
        public string Name { get; set; } = "";

        /// <summary>Where a call goes while the switch is off.</summary>
        public DestinationLabel Normal { get; set; } = new();

        /// <summary>Where a call goes while the switch is on.</summary>
        public DestinationLabel Override { get; set; } = new();

        /// <summary>Which way the switch is set right now, as astdb says, or Unknown without AMI.</summary>
        public CallFlowState State { get; set; }

        public string StateCssClass => this.State switch
        {
            CallFlowState.Normal => "text-bg-success",
            CallFlowState.Override => "text-bg-warning",
            _ => "bg-body-secondary text-body-secondary border",
        };

        public string StateText => this.State switch
        {
            CallFlowState.Normal => "Normal",
            CallFlowState.Override => "Override",
            _ => "Unknown",
        };

        public string StateTitle => this.State switch
        {
            CallFlowState.Normal => "Switched off: calls go to the normal destination.",
            CallFlowState.Override => $"Switched on: calls go to the override destination. Dial {this.FeatureCode} to switch it back.",
            _ => "Asterisk could not be asked over AMI.",
        };
    }
}
