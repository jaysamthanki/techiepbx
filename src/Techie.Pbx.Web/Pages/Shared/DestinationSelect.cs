using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Shared
{
    /// <summary>
    /// What the shared "where does this call go?" picker needs: the choices to offer, the form
    /// field to post as, and what is chosen now. Built by whichever page is asking — inbound
    /// routes, IVR keys, ring group failover — from
    /// <see cref="DestinationCatalog.All"/> (D35).
    ///
    /// The posted value is <see cref="Destination.Key"/>, which
    /// <see cref="Destination.TryParse"/> reads back; a page never has to know the format.
    /// </summary>
    public class DestinationSelect
    {
        public List<DestinationChoice> Choices { get; set; } = new();

        public string ElementID { get; set; } = "destination";

        /// <summary>
        /// The chosen destination when it is not on the list any more, because what it pointed at
        /// was deleted, disabled or had its voicemail switched off. Shown as its own entry rather
        /// than quietly falling back to the first choice: a call route silently repointing itself
        /// is worse than an admin having to fix it.
        /// </summary>
        public string? MissingKey =>
            this.SelectedKey != null && !this.Choices.Any(c => string.Equals(c.Destination.Key, this.SelectedKey, StringComparison.Ordinal))
                ? this.SelectedKey
                : null;

        public string Name { get; set; } = "destination";

        /// <summary>The "nothing chosen yet" entry. Null leaves it out.</summary>
        public string? Placeholder { get; set; } = "Choose a destination…";

        public bool Required { get; set; } = true;

        /// <summary>The <see cref="Destination.Key"/> that is chosen, or null for none.</summary>
        public string? SelectedKey { get; set; }

        public bool IsSelected(DestinationChoice choice) =>
            string.Equals(choice.Destination.Key, this.SelectedKey, StringComparison.Ordinal);
    }
}
