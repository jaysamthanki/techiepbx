using System.Globalization;
using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// One assignable key on a desk phone (D121): the position of the key, and what it watches and
    /// dials. A reference, never a copy — "extension 1001", not a snapshot of extension 1001 — for
    /// the same reason a <see cref="Destination"/> is (D35).
    ///
    /// Two kinds exist today, <see cref="PhoneButtonTarget"/> names them, and the storage takes a
    /// third without a schema change. What a key does on the phone is the provisioning renderer's
    /// business; what a key may point at is this class's.
    /// </summary>
    public partial class PhoneButton
    {
        /// <summary>
        /// How many keys a phone is offered. Eight is what the form shows and what a save replaces
        /// wholesale; it is not a property of the handset, which may well have more line keys than
        /// this or fewer (D121).
        /// </summary>
        public const int Count = 8;

        /// <summary>The first key. Positions are 1-based because the label on the form is "Key 1".</summary>
        public const int FirstPosition = 1;

        /// <summary>Whether this key has anything on it. A key that has not is stored as no row.</summary>
        public bool IsAssigned => this.TargetType.Length > 0;

        /// <summary>
        /// The target as one string, for a select option: "Extension:1001", "ParkingSlot:3", or
        /// empty for a key nobody has assigned. The same shape <see cref="Destination.Key"/> uses,
        /// and read back by <see cref="TryParse"/>.
        /// </summary>
        public string Key => this.IsAssigned ? $"{this.TargetType}:{this.TargetValue}" : PhoneButtonTarget.None;

        public long PhoneButtonID { get; set; }

        public long PhoneID { get; set; }

        /// <summary>Which key this is, 1 to <see cref="Count"/>.</summary>
        public int Position { get; set; }

        /// <summary>A <see cref="PhoneButtonTarget"/> value.</summary>
        public string TargetType { get; set; } = PhoneButtonTarget.None;

        /// <summary>
        /// What the kind points at: an extension number, or a parking slot number. It is also what
        /// the phone subscribes to and dials, because both are extensions of the internal context
        /// in the generated dialplan — the slot's lamp works because that context carries a hint
        /// for it (D121).
        /// </summary>
        public string TargetValue { get; set; } = "";

        /// <summary>
        /// What the key says on the phone's screen. An extension is named by its own name, falling
        /// back to the number for an extension that has been deleted out from under the key; a slot
        /// is named by what it is, because a parking slot has no name of its own.
        /// </summary>
        public string Label(IEnumerable<Extension> extensions)
        {
            if (string.Equals(this.TargetType, PhoneButtonTarget.ParkingSlot, StringComparison.Ordinal))
                return $"Park {this.TargetValue}";

            var extension = extensions.FirstOrDefault(e =>
                string.Equals(e.Number, this.TargetValue, StringComparison.Ordinal));

            return extension is { Name.Length: > 0 } ? extension.Name : this.TargetValue;
        }

        /// <summary>
        /// Reads back what <see cref="Key"/> wrote, for the given position. Anything that is not a
        /// kind we know, or that does not validate, is refused rather than guessed at — a key we
        /// cannot read is a lamp watching the wrong thing. A blank key is not a failure worth
        /// reporting, it is a key nobody assigned, so it returns false with nothing to say.
        /// </summary>
        public static bool TryParse(string? key, int position, out PhoneButton button)
        {
            button = new PhoneButton { Position = position };

            if (string.IsNullOrWhiteSpace(key))
                return false;

            var separator = key.IndexOf(':');
            if (separator < 0)
                return false;

            var parsed = new PhoneButton
            {
                Position = position,
                TargetType = key[..separator],
                TargetValue = key[(separator + 1)..],
            };

            if (parsed.Validate().Count > 0)
                return false;

            button = parsed;
            return true;
        }

        /// <summary>
        /// The keys that can actually do something, in key order: the target has to still be there.
        /// An extension that has been deleted or switched off has no PJSIP endpoint to watch, and a
        /// parking slot outside the configured lot — parking switched off, or fewer slots than it
        /// once had — has no hint to subscribe to. Either would be a dark key at best and a key
        /// that dials a number that does not exist at worst, so neither is written to a phone.
        /// </summary>
        public static List<PhoneButton> Usable(
            IEnumerable<PhoneButton> buttons,
            IEnumerable<Extension> extensions,
            IEnumerable<int> parkingSlots)
        {
            var enabled = extensions.Where(e => e.Enabled).ToList();
            var slots = parkingSlots.ToList();

            return buttons
                .Where(b => b.IsAssigned && b.Validate().Count == 0)
                .Where(b => string.Equals(b.TargetType, PhoneButtonTarget.ParkingSlot, StringComparison.Ordinal)
                    ? slots.Contains(int.Parse(b.TargetValue, CultureInfo.InvariantCulture))
                    : enabled.Any(e => string.Equals(e.Number, b.TargetValue, StringComparison.Ordinal)))
                .OrderBy(b => b.Position)
                .ToList();
        }

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (this.Position is < FirstPosition || this.Position > Count)
                errors.Add($"A phone key must be numbered {FirstPosition} to {Count}.");

            if (!PhoneButtonTarget.IsKnown(this.TargetType))
            {
                errors.Add($"'{this.TargetType}' is not something a phone key can be pointed at.");
                return errors;
            }

            if (string.Equals(this.TargetType, PhoneButtonTarget.Extension, StringComparison.Ordinal) &&
                !Extension.IsValidNumber(this.TargetValue))
            {
                errors.Add("A key on an extension needs an extension number of 2 to 6 digits.");
            }

            // One digit, 1-9, because that is what a parking slot is: it is retrieved by dialling
            // it, and the dialplan only has single digits to spare (D119). Whether the lot really
            // has that slot is a question for the settings, and Usable is what asks it.
            if (string.Equals(this.TargetType, PhoneButtonTarget.ParkingSlot, StringComparison.Ordinal) &&
                !SlotPattern().IsMatch(this.TargetValue))
            {
                errors.Add("A key on a parking slot needs a slot number of 1 to 9.");
            }

            return errors;
        }

        [GeneratedRegex(@"^[1-9]$")]
        private static partial Regex SlotPattern();
    }
}
