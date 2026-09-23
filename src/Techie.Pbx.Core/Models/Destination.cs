namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// Where a call goes: a kind and, for the kinds that need one, what it points at. A reference,
    /// never a copy — "extension 1001", not a snapshot of extension 1001 (D35).
    ///
    /// Features that let an admin choose a destination (inbound routes, IVR keys, ring group
    /// failover) store these two fields in two columns of their own table. This class is what
    /// they hand to the dialplan helper and to the picker.
    /// </summary>
    public class Destination
    {
        /// <summary>Ending the call is always a choice, and needs nothing to point at.</summary>
        public static Destination Hangup => new(DestinationType.Hangup);

        /// <summary>
        /// The destination as one string, for a select option or a URL. "Extension:1001", or just
        /// "Hangup" when there is nothing to point at.
        /// </summary>
        public string Key => this.Value.Length == 0 ? this.Type.ToString() : $"{this.Type}:{this.Value}";

        public DestinationType Type { get; set; }

        /// <summary>The extension number for Extension and Voicemail; empty for Hangup.</summary>
        public string Value { get; set; } = "";

        public Destination()
        {
        }

        public Destination(DestinationType type, string value = "")
        {
            this.Type = type;
            this.Value = value;
        }

        /// <summary>
        /// Reads back what <see cref="Key"/> wrote. Anything that is not a kind we know, or that
        /// does not validate, is rejected rather than guessed at: a destination we cannot read is
        /// a call we would send to the wrong place.
        /// </summary>
        public static bool TryParse(string? key, out Destination destination)
        {
            destination = new Destination();

            if (string.IsNullOrWhiteSpace(key))
                return false;

            var separator = key.IndexOf(':');
            var typeName = separator < 0 ? key : key[..separator];
            var value = separator < 0 ? "" : key[(separator + 1)..];

            if (!Enum.TryParse<DestinationType>(typeName, ignoreCase: false, out var type))
                return false;

            var parsed = new Destination(type, value);
            if (parsed.Validate().Count > 0)
                return false;

            destination = parsed;
            return true;
        }

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            switch (this.Type)
            {
                case DestinationType.Extension:
                case DestinationType.Voicemail:
                    if (!Extension.IsValidNumber(this.Value))
                        errors.Add($"A {this.Type} destination needs an extension number of 2 to 6 digits.");
                    break;

                // A ring group's number follows the same rule as an extension's, because it is
                // dialled the same way and lives in the same context (D54).
                case DestinationType.RingGroup:
                    if (!Extension.IsValidNumber(this.Value))
                        errors.Add("A RingGroup destination needs a group number of 2 to 6 digits.");
                    break;

                // And so does an announcement's play extension, for the same reason (D56).
                case DestinationType.Announcement:
                    if (!Extension.IsValidNumber(this.Value))
                        errors.Add("An Announcement destination needs a play extension of 2 to 6 digits.");
                    break;

                // And an IVR's, which is the number a caller dials to hear the menu (D59).
                case DestinationType.Ivr:
                    if (!Extension.IsValidNumber(this.Value))
                        errors.Add("An IVR destination needs a play extension of 2 to 6 digits.");
                    break;

                // And a time condition's, which is the number that runs the open/closed check (D63).
                case DestinationType.TimeCondition:
                    if (!Extension.IsValidNumber(this.Value))
                        errors.Add("A time condition destination needs a play extension of 2 to 6 digits.");
                    break;

                // A call flow control is pointed at by its feature code, a star and two or three
                // digits (F9): the only destination whose value is not a plain number.
                case DestinationType.CallFlowControl:
                    if (!CallFlowControl.IsValidFeatureCode(this.Value))
                        errors.Add("A call flow control destination needs a feature code: a * and 2 or 3 digits.");
                    break;

                case DestinationType.Hangup:
                    if (this.Value.Length > 0)
                        errors.Add("A Hangup destination has nothing to point at.");
                    break;

                default:
                    errors.Add($"'{this.Type}' is not a destination this system knows.");
                    break;
            }

            return errors;
        }
    }
}
