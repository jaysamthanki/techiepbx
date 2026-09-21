using System.Globalization;
using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// One assignable key on a desk phone (D121): the position of the key, and what it watches and
    /// dials. A reference, never a copy — "extension 1001", not a snapshot of extension 1001 — for
    /// the same reason a <see cref="Destination"/> is (D35).
    ///
    /// The first key is the phone's own registration, <see cref="PhoneButtonTarget.Line"/>: a phone
    /// registers as whatever its leading keys say, and as nothing else. That is why there is no
    /// extension on <see cref="Phone"/> any more — one key on the Buttons tab is one key on the
    /// handset, and the registration was the ninth line nobody had asked for (schema 020).
    ///
    /// Three kinds exist today, <see cref="PhoneButtonTarget"/> names them, and the storage takes a
    /// fourth without a schema change. What a key does on the phone is the provisioning renderer's
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

        /// <summary>Whether this key is the phone's own registration rather than a lamp.</summary>
        public bool IsLine => string.Equals(this.TargetType, PhoneButtonTarget.Line, StringComparison.Ordinal);

        /// <summary>
        /// The target as one string, for a select option: "Line:1001", "Blf:1002", "ParkingSlot:3",
        /// or empty for a key nobody has assigned. The same shape <see cref="Destination.Key"/>
        /// uses, and read back by <see cref="TryParse"/>.
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
        /// The extension this phone registers as, or null when no key is a line — an auto-added
        /// phone nobody has assigned yet. This is what replaced <c>Phone.ExtensionID</c>, so
        /// everything that used to ask the row asks the keys instead.
        /// </summary>
        public static string? LineNumber(IEnumerable<PhoneButton> buttons) =>
            Lines(buttons).FirstOrDefault()?.TargetValue;

        /// <summary>
        /// The line keys, in key order: the registrations this phone is given, first one first.
        /// Usually exactly one.
        /// </summary>
        public static List<PhoneButton> Lines(IEnumerable<PhoneButton> buttons) =>
            buttons.Where(b => b.IsLine).OrderBy(b => b.Position).ToList();

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
        ///
        /// A phone left with no line key is left with no keys at all: the lamps subscribe and dial
        /// on the registration, so without one there is nothing for them to be on.
        /// </summary>
        public static List<PhoneButton> Usable(
            IEnumerable<PhoneButton> buttons,
            IEnumerable<Extension> extensions,
            IEnumerable<int> parkingSlots)
        {
            var enabled = extensions.Where(e => e.Enabled).ToList();
            var slots = parkingSlots.ToList();

            var usable = buttons
                .Where(b => b.IsAssigned && b.Validate().Count == 0)
                .Where(b => string.Equals(b.TargetType, PhoneButtonTarget.ParkingSlot, StringComparison.Ordinal)
                    ? slots.Contains(int.Parse(b.TargetValue, CultureInfo.InvariantCulture))
                    : enabled.Any(e => string.Equals(e.Number, b.TargetValue, StringComparison.Ordinal)))
                .OrderBy(b => b.Position)
                .ToList();

            return usable.Any(b => b.IsLine) ? usable : new List<PhoneButton>();
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

            if (PhoneButtonTarget.IsExtension(this.TargetType) && !Extension.IsValidNumber(this.TargetValue))
                errors.Add("A key on an extension needs an extension number of 2 to 6 digits.");

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

        /// <summary>
        /// The rules that are about the set of keys rather than any one of them, which is where the
        /// registration lives now (schema 020):
        ///
        /// <list type="bullet">
        /// <item>Every key is valid in itself, and no two are the same key.</item>
        /// <item><b>Key 1 is a line.</b> A phone has to register as something, and a handset shows
        /// its own line on its first key whatever we say, so a set that starts anywhere else is a
        /// set that would not look like the form that wrote it.</item>
        /// <item><b>Lines lead.</b> A second or third registration takes the next key down; a line
        /// after a lamp would push that lamp along on the handset and mean something different
        /// there than it does here.</item>
        /// </list>
        ///
        /// Whether those extensions exist, and whether another phone has already claimed one as
        /// its line, needs the database: <c>PhoneButtonRepository</c> asks that.
        /// </summary>
        public static List<string> ValidateSet(IEnumerable<PhoneButton> buttons)
        {
            var ordered = buttons.OrderBy(b => b.Position).ToList();
            var errors = new List<string>();

            foreach (var button in ordered)
            {
                foreach (var error in button.Validate())
                    errors.Add($"Key {button.Position}: {error}");
            }

            if (ordered.Select(b => b.Position).Distinct().Count() != ordered.Count)
                errors.Add("Two keys cannot be in the same place.");

            var lines = ordered.Count(b => b.IsLine);

            if (lines == 0 || ordered[0].Position != FirstPosition)
            {
                errors.Add($"Key {FirstPosition} has to be the extension this phone registers as.");
                return errors;
            }

            for (var index = 0; index < ordered.Count; index++)
            {
                if (ordered[index].IsLine == (index < lines))
                    continue;

                errors.Add($"Key {ordered[index].Position}: only the first keys on a phone can be lines it registers as.");
            }

            return errors;
        }

        [GeneratedRegex(@"^[1-9]$")]
        private static partial Regex SlotPattern();
    }
}
