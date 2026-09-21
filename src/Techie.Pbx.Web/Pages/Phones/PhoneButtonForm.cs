namespace Techie.Pbx.Web.Pages.Phones
{
    /// <summary>
    /// One row of the Buttons tab: a key, and what is on it. The form carries a row for every key
    /// the phone is offered, in order, so there is nothing to add or remove and no JavaScript to
    /// do it with — the same shape the IVR digit map uses (D59). A row left on "Nothing" is
    /// dropped when the phone is saved, because an unassigned key is simply absent (D121).
    /// </summary>
    public class PhoneButtonForm
    {
        /// <summary>Which key this is, 1 to <c>PhoneButton.Count</c>. Posted hidden.</summary>
        public int Position { get; set; }

        /// <summary>
        /// What is on the key, as <c>PhoneButton.Key</c> writes it: "Extension:1001",
        /// "ParkingSlot:3", or empty for a key nobody has assigned.
        /// </summary>
        public string? Target { get; set; } = "";
    }
}
