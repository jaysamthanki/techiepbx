using Techie.Pbx.Asterisk.Ami;

namespace Techie.Pbx.Web.Pages.Extensions
{
    /// <summary>
    /// The live registration badge for one extension. The same partial renders it inside the
    /// table and, with <see cref="OutOfBand"/> set, as the answer to the five second poll, which
    /// htmx swaps into the row it belongs to.
    /// </summary>
    public class StatusBadge
    {
        public string CssClass => this.State switch
        {
            RegistrationState.Registered => "text-bg-success",
            RegistrationState.Unreachable => "text-bg-warning",
            RegistrationState.NotRegistered => "text-bg-secondary",
            _ => "text-bg-light border",
        };

        /// <summary>The element id the poll swaps into, so both renderings have to agree on it.</summary>
        public string ElementID => $"extension-status-{this.Number}";

        public string Number { get; set; } = "";
        public bool OutOfBand { get; set; }
        public RegistrationState State { get; set; }

        public string Text => this.State switch
        {
            RegistrationState.Registered => "Registered",
            RegistrationState.Unreachable => "Unreachable",
            RegistrationState.NotRegistered => "Not registered",
            _ => "Unknown",
        };

        /// <summary>Why the badge says what it says, as a tooltip.</summary>
        public string Title => this.State switch
        {
            RegistrationState.Registered => "A phone is registered against this extension.",
            RegistrationState.Unreachable => "A phone registered but has stopped answering Asterisk.",
            RegistrationState.NotRegistered => "No phone has registered against this extension.",
            _ => "Asterisk could not be asked over AMI.",
        };
    }
}
