using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Phones
{
    /// <summary>
    /// What the edit form in the modal shows, and what it posts back. The form has two tabs (D121):
    /// Buttons, which is what an admin comes here to change most often and so is the one that opens,
    /// and Details.
    ///
    /// Three things are posted — the keys, the name and the enabled flag — because everything else
    /// about a phone was written by the phone, and an admin editing it would only be editing a
    /// record of what happened (D78). The rest is shown, read-only, so the person assigning a phone
    /// can see which handset they are looking at.
    ///
    /// <b>There is no extension field.</b> Key 1 is the extension the phone registers as, so the
    /// Details tab does not ask a second time (schema 020).
    /// </summary>
    public class PhoneForm
    {
        public string Brand { get; set; } = "";

        /// <summary>
        /// The Buttons tab: one row per assignable key, in key order, posted back whole because a
        /// save replaces every key (D121).
        /// </summary>
        public List<PhoneButtonForm> Buttons { get; set; } = new();

        /// <summary>
        /// The call flow controls a key may be put on (F9): the key's lamp is lit while the switch
        /// is on, and pressing it flips it. Not posted back.
        /// </summary>
        public List<CallFlowControl> CallFlowControls { get; set; } = new();

        public bool Enabled { get; set; } = true;
        public List<string> Errors { get; set; } = new();

        /// <summary>Every extension a key on this phone could name. Not posted back.</summary>
        public List<Extension> Extensions { get; set; } = new();

        public string Firmware { get; set; } = "";
        public string LastConfig { get; set; } = "";
        public string LastIP { get; set; } = "";

        /// <summary>
        /// The port this phone is told to speak SIP from, shown because it is the first thing to
        /// check when two phones behind one NAT misbehave (D81).
        /// </summary>
        public int LocalSipPort { get; set; }

        public string Mac { get; set; } = "";
        public string Model { get; set; } = "";
        public string? Name { get; set; } = "";

        /// <summary>
        /// The parking slots a key may be put on: 1..slots when parking is switched on, and empty
        /// when it is not, so the dropdowns offer a slot only when there is a lot to park in
        /// (D119, D121). Not posted back.
        /// </summary>
        public List<int> ParkingSlots { get; set; } = new();

        public long PhoneID { get; set; }

        /// <summary>
        /// Why this phone cannot be rebooted, or empty when it can (D123). A reboot is a SIP NOTIFY
        /// to whatever contact the phone registered, so it needs a line key to address and a phone
        /// actually registered on it; the footer shows the button disabled with this as its tooltip
        /// rather than hiding it, so that the reason is readable rather than guessed at.
        /// </summary>
        public string RebootHint { get; set; } = "";
    }
}
