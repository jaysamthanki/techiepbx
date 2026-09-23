using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// A number that rings several extensions (F3). Dialled like an extension, from a phone or
    /// from an inbound route, and when nobody answers the call goes to the group's destination.
    ///
    /// Members are an ordered list of extension numbers in one column rather than a table of their
    /// own (D53), and the no-answer destination is the two fields D35 settled on.
    /// </summary>
    public partial class RingGroup
    {
        /// <summary>The longest a group may ring one attempt for, in seconds.</summary>
        public const int MaxRingSeconds = 300;

        /// <summary>The shortest, below which a phone barely gets to ring at all.</summary>
        public const int MinRingSeconds = 5;

        /// <summary>
        /// Put in front of the caller's name so whoever picks up knows which group rang, e.g.
        /// "Sales: ". Written as typed, separator and all.
        /// </summary>
        public string CallerIDPrefix { get; set; } = "";

        /// <summary>The kind of destination for a call nobody answered, by name (D35).</summary>
        public string DestinationType { get; set; } = Models.DestinationType.Hangup.ToString();

        /// <summary>What that destination points at. Empty for Hangup.</summary>
        public string DestinationValue { get; set; } = "";

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// The extension numbers to ring, in order, comma separated. Order is what Hunt follows;
        /// All ignores it.
        /// </summary>
        public string Members { get; set; } = "";

        /// <summary>What the group is called, for the admin and for the dialplan comment.</summary>
        public string Name { get; set; } = "";

        /// <summary>The number people dial to reach the group, like an extension number.</summary>
        public string Number { get; set; } = "";

        public long RingGroupID { get; set; }

        /// <summary>
        /// How long one attempt rings. For Hunt that is per member, not shared out between them:
        /// a group of four with 20 seconds rings for up to 80 (D52).
        /// </summary>
        public int RingSeconds { get; set; } = 20;

        /// <summary>"All" or "Hunt", by name (D53).</summary>
        public string Strategy { get; set; } = RingStrategy.All.ToString();

        /// <summary>The two destination columns as the one string the picker posts (D35).</summary>
        public string DestinationKey() =>
            this.DestinationValue.Length == 0 ? this.DestinationType : $"{this.DestinationType}:{this.DestinationValue}";

        /// <summary>The members as a list, in order, ignoring blanks.</summary>
        public List<string> MemberList() =>
            this.Members.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        /// <summary>
        /// Where a call nobody answered goes. Never null: a pair that does not read back becomes
        /// Hangup, because a call has to end somewhere and nowhere is worse than here.
        /// </summary>
        public Destination ToDestination() =>
            Destination.TryParse(this.DestinationKey(), out var destination) ? destination : Destination.Hangup;

        /// <summary>The strategy, which <see cref="Validate"/> has already checked is one we know.</summary>
        public RingStrategy ToStrategy() =>
            Enum.TryParse<RingStrategy>(this.Strategy, out var strategy) ? strategy : RingStrategy.All;

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            // The same rule as an extension number, because it is dialled the same way and shares
            // the same context. Feature codes all start with *, so digits cannot collide with one;
            // collisions with a real extension are checked by the repository, which can see them.
            if (!Extension.IsValidNumber(Number))
                errors.Add("Number must be 2 to 6 digits.");

            if (string.IsNullOrWhiteSpace(Name))
                errors.Add("Name is required.");
            else if (Name.Length > 64)
                errors.Add("Name must be 64 characters or fewer.");
            else if (!NamePattern().IsMatch(Name))
                errors.Add("Name may only contain letters, digits, spaces and . , ' - _ ( ) &");

            if (CallerIDPrefix.Length > 16)
                errors.Add("Caller ID prefix must be 16 characters or fewer.");
            else if (CallerIDPrefix.Length > 0 && !PrefixPattern().IsMatch(CallerIDPrefix))
                errors.Add("Caller ID prefix may only contain letters, digits, spaces and . , : ' - _ ( ) &");

            var members = MemberList();
            if (members.Count == 0)
                errors.Add("A ring group needs at least one member.");

            foreach (var member in members.Where(m => !Extension.IsValidNumber(m)))
                errors.Add($"'{member}' is not an extension number.");

            if (members.Count != members.Distinct(StringComparer.Ordinal).Count())
                errors.Add("A member can only be in the group once.");

            if (members.Contains(Number, StringComparer.Ordinal))
                errors.Add("A ring group cannot be a member of itself.");

            if (!Enum.TryParse<RingStrategy>(Strategy, out _))
                errors.Add($"'{Strategy}' is not a ringing strategy this system knows.");

            if (RingSeconds is < MinRingSeconds or > MaxRingSeconds)
                errors.Add($"Ring time must be between {MinRingSeconds} and {MaxRingSeconds} seconds.");

            if (!Destination.TryParse(DestinationKey(), out var destination))
                errors.Add("Choose where a call nobody answers should go.");
            else if (destination.Type == Models.DestinationType.RingGroup && destination.Value == Number)
                errors.Add("A ring group cannot send its unanswered calls to itself.");

            return errors;
        }

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex NamePattern();

        /// <summary>A prefix usually ends in a separator, so a colon is allowed as well.</summary>
        [GeneratedRegex(@"^[\p{L}\p{N} .,:'\-_()&]+\z")]
        private static partial Regex PrefixPattern();
    }
}
