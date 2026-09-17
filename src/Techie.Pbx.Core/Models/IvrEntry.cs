namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// One key of an IVR's digit map: "press 2 and the call goes there" (D59). A digit with no
    /// entry is not a setting, it is simply absent — the caller who presses it gets the IVR's
    /// invalid handling, so there is nothing to store for the keys nobody uses.
    ///
    /// The destination is the two fields D35 settled on, exactly as a ring group's failover is.
    /// </summary>
    public class IvrEntry
    {
        /// <summary>
        /// Every key a phone can send, in the order a menu should be listed in. A caller has a
        /// keypad and nothing else, so this is the whole alphabet an IVR can be built from.
        /// </summary>
        public const string Keypad = "0123456789*#";

        /// <summary>The kind of destination, by name (D35).</summary>
        public string DestinationType { get; set; } = "";

        /// <summary>What that destination points at. Empty for Hangup.</summary>
        public string DestinationValue { get; set; } = "";

        /// <summary>The single key this entry answers to: 0-9, * or #.</summary>
        public string Digit { get; set; } = "";

        public long IvrEntryID { get; set; }

        public long IvrID { get; set; }

        /// <summary>The two destination columns as the one string the picker posts (D35).</summary>
        public string DestinationKey() =>
            this.DestinationValue.Length == 0 ? this.DestinationType : $"{this.DestinationType}:{this.DestinationValue}";

        /// <summary>Whether this is a key a caller could actually press.</summary>
        public static bool IsValidDigit(string digit) =>
            digit.Length == 1 && Keypad.Contains(digit[0]);

        /// <summary>
        /// Where a digit sorts in a menu: 0 to 9, then * and #. Ordinal string order would put '#'
        /// and '*' before the digits, which is not how anyone reads a menu.
        /// </summary>
        public static int Rank(string digit) =>
            digit.Length == 1 ? Keypad.IndexOf(digit[0]) : Keypad.Length;

        /// <summary>Where this key sends a call. Never null: an unreadable pair reads back as Hangup.</summary>
        public Destination ToDestination() =>
            Destination.TryParse(this.DestinationKey(), out var destination) ? destination : Destination.Hangup;

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (!IsValidDigit(Digit))
                errors.Add($"'{Digit}' is not a key a caller can press. Use 0-9, * or #.");

            if (!Destination.TryParse(DestinationKey(), out _))
                errors.Add($"Choose where key {Digit} should send a call.");

            return errors;
        }
    }
}
