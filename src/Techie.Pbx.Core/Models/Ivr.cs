using System.Globalization;
using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// An auto attendant (F6): a greeting, a digit map, and somewhere for a caller who pressed
    /// nothing usable to end up.
    ///
    /// The greeting is a <b>reference to an announcement</b>, not audio of its own (D58): the IVR
    /// plays an announcement, so there is one audio store, one upload path and one set of checks.
    /// The optional play extension is what makes the menu dialable and what a destination points
    /// at, exactly as an announcement's does (D57).
    /// </summary>
    public partial class Ivr
    {
        /// <summary>The most times a caller can be sent round the menu again before giving up.</summary>
        public const int MaxRetries = 10;

        /// <summary>The longest we will wait for a caller to press something, in seconds.</summary>
        public const int MaxTimeoutSeconds = 60;

        /// <summary>The shortest, below which the menu barely finishes before it gives up.</summary>
        public const int MinTimeoutSeconds = 1;

        /// <summary>The announcement whose audio is this menu's greeting (D58). Required.</summary>
        public long AnnouncementID { get; set; }

        /// <summary>What the menu is for, in an admin's words. Ends up as a dialplan comment.</summary>
        public string Description { get; set; } = "";

        /// <summary>The kind of destination for a caller who chose nothing, by name (D35).</summary>
        public string DestinationType { get; set; } = Models.DestinationType.Hangup.ToString();

        /// <summary>What that destination points at. Empty for Hangup.</summary>
        public string DestinationValue { get; set; } = "";

        /// <summary>
        /// Whether a caller may dial an extension number straight from the menu instead of
        /// choosing a key. Off by default: it is a convenience, not something to switch on for a
        /// site without meaning to (D60).
        /// </summary>
        public bool EnableDirectDial { get; set; }

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// The digit map. Not a column: these are rows in <c>IvrEntries</c>, loaded and written
        /// with the IVR because they are only ever read together.
        /// </summary>
        public List<IvrEntry> Entries { get; set; } = new();

        public long IvrID { get; set; }

        /// <summary>What the menu is called. Unique, and what an admin picks it by.</summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// The number to dial to hear the menu, or empty for none. Optional, and without it the
        /// IVR has no dialplan entry and cannot be a destination — the same rule, for the same
        /// reason, as an announcement's (D57).
        /// </summary>
        public string PlayExtension { get; set; } = "";

        /// <summary>
        /// How many times a caller who presses nothing, or something with no key behind it, is
        /// taken back to the greeting before the call goes to the final destination.
        /// </summary>
        public int Retries { get; set; } = 3;

        /// <summary>
        /// Whether a key that sends the caller to an announcement brings them back to this menu
        /// once it has played, instead of hanging up (piece 37). Off by default, and off is the
        /// dialplan as it always was. Announcement keys only: the final destination never returns,
        /// because a caller who pressed nothing would then go round the menu for ever (D59).
        /// </summary>
        public bool ReturnAfterAnnouncement { get; set; }

        /// <summary>
        /// How long the menu waits for a key after the greeting, and how long it waits between
        /// digits of a directly dialled extension.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 10;

        /// <summary>
        /// The dialplan context this menu's keys live in. One per IVR, named by ID because the ID
        /// never changes and a menu name is free text a context name could not survive (D59).
        /// </summary>
        public string Context => "ivr-" + this.IvrID.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Whether a call can be sent here: switched on, and with a number to send it to. Whether
        /// the greeting is actually playable needs the announcement, so the catalog and the
        /// renderer check that as well.
        /// </summary>
        public bool IsPlayable => this.Enabled && this.PlayExtension.Length > 0;

        /// <summary>The two destination columns as the one string the picker posts (D35).</summary>
        public string DestinationKey() =>
            this.DestinationValue.Length == 0 ? this.DestinationType : $"{this.DestinationType}:{this.DestinationValue}";

        /// <summary>
        /// The announcement this menu greets with, or null when it points at one that is gone,
        /// switched off or has no audio yet — in which case there is no menu to render (D58).
        /// </summary>
        public Announcement? GreetingIn(IEnumerable<Announcement> announcements) =>
            announcements.FirstOrDefault(a => a.AnnouncementID == this.AnnouncementID && a.Enabled && a.HasAudio);

        /// <summary>The IVR as a destination, for the catalog and the dialplan (D59).</summary>
        public Destination ToDestination() => new(Models.DestinationType.Ivr, this.PlayExtension);

        /// <summary>
        /// Where a caller who chose nothing ends up. Never null: a pair that does not read back
        /// becomes Hangup, because a call has to end somewhere and nowhere is worse than here.
        /// </summary>
        public Destination ToFinalDestination() =>
            Destination.TryParse(this.DestinationKey(), out var destination) ? destination : Destination.Hangup;

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(Name))
                errors.Add("Name is required.");
            else if (Name.Length > 64)
                errors.Add("Name must be 64 characters or fewer.");
            else if (!NamePattern().IsMatch(Name))
                errors.Add("Name may only contain letters, digits, spaces and . , ' - _ ( ) &");

            if (Description.Length > 128)
                errors.Add("Description must be 128 characters or fewer.");
            else if (Description.Length > 0 && !DescriptionPattern().IsMatch(Description))
                errors.Add("Description may only contain letters, digits, spaces and . , ' - _ ( ) &");

            if (AnnouncementID <= 0)
                errors.Add("An IVR needs an announcement to play as its greeting.");

            // The same rule as an extension number, because it is dialled the same way and shares
            // the same context. Feature codes all start with *, so digits cannot collide with one;
            // collisions with a real extension, a ring group or an announcement are checked by the
            // repository, which can see them.
            if (PlayExtension.Length > 0 && !Extension.IsValidNumber(PlayExtension))
                errors.Add("Play extension must be 2 to 6 digits, or blank for none.");

            if (TimeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds)
                errors.Add($"Timeout must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds} seconds.");

            if (Retries is < 0 or > MaxRetries)
                errors.Add($"Retries must be between 0 and {MaxRetries}.");

            foreach (var entry in Entries)
                errors.AddRange(entry.Validate());

            var digits = Entries.Select(e => e.Digit).ToList();
            if (digits.Count != digits.Distinct(StringComparer.Ordinal).Count())
                errors.Add("A key can only appear once in the digit map.");

            if (!Destination.TryParse(DestinationKey(), out var final))
                errors.Add("Choose where a caller who presses nothing should go.");
            else if (final.Type == Models.DestinationType.Ivr && final.Value == PlayExtension)
                errors.Add("An IVR cannot send a caller who presses nothing back to itself.");

            return errors;
        }

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex DescriptionPattern();

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex NamePattern();
    }
}
