using Techie.Pbx.Asterisk.Ami;

namespace Techie.Pbx.Web.Pages.Shared
{
    /// <summary>
    /// A live registration badge, for an extension (a phone registered with us) or a trunk (us
    /// registered with a provider). The same partial renders it inside a table and, with
    /// <see cref="OutOfBand"/> set, as the answer to the five second poll, which htmx swaps into
    /// the row it belongs to.
    /// </summary>
    public class StatusBadge
    {
        public string CssClass => this.State switch
        {
            RegistrationState.Registered => "text-bg-success",
            RegistrationState.Rejected => "text-bg-danger",
            RegistrationState.Unreachable => "text-bg-warning",
            RegistrationState.NotRegistered => "text-bg-secondary",
            // Not text-bg-light: "light" is a fixed near white in both themes, so on a dark page
            // the one badge that means "no answer" would be the brightest thing in the table.
            _ => "bg-body-secondary text-body-secondary border",
        };

        /// <summary>The element id the poll swaps into, so both renderings have to agree on it.</summary>
        public string ElementID { get; set; } = "";

        /// <summary>Which way round the registration goes, which changes what the tooltip says.</summary>
        public bool IsTrunk { get; set; }

        public bool OutOfBand { get; set; }
        public RegistrationState State { get; set; }

        public string Text => this.State switch
        {
            RegistrationState.Registered => "Registered",
            RegistrationState.Rejected => "Rejected",
            RegistrationState.Unreachable => "Unreachable",
            RegistrationState.NotRegistered => "Not registered",
            _ => "Unknown",
        };

        /// <summary>Why the badge says what it says, as a tooltip.</summary>
        public string Title => this.State switch
        {
            RegistrationState.Registered => this.IsTrunk
                ? "The provider has accepted our registration."
                : "A phone is registered against this extension.",
            RegistrationState.Rejected => "The provider refused our credentials. Check the username and password.",
            RegistrationState.Unreachable => this.IsTrunk
                ? "The provider registered us but has stopped answering."
                : "A phone registered but has stopped answering Asterisk.",
            RegistrationState.NotRegistered => this.IsTrunk
                ? "This trunk is not registered with its provider."
                : "No phone has registered against this extension.",
            _ => "Asterisk could not be asked over AMI.",
        };

        public static StatusBadge ForExtension(string number, RegistrationState state, bool outOfBand = false) => new()
        {
            ElementID = $"extension-status-{number}",
            OutOfBand = outOfBand,
            State = state,
        };

        public static StatusBadge ForTrunk(string name, RegistrationState state, bool outOfBand = false) => new()
        {
            ElementID = $"trunk-status-{name}",
            IsTrunk = true,
            OutOfBand = outOfBand,
            State = state,
        };
    }
}
