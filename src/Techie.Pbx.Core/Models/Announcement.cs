using System.Text;
using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// A recorded message a caller hears and then the call ends. The audio lives on disk as one
    /// converted WAV (D55); what is stored here is the name, the file that name produced, and the
    /// optional number a user can dial to listen to it.
    ///
    /// An announcement with a play extension is also a destination (D56), so an inbound route, a
    /// ring group's failover or a future IVR key can send a call to it.
    /// </summary>
    public partial class Announcement
    {
        /// <summary>
        /// The extension every stored file gets. One format in, one format out: whatever was
        /// uploaded, what Asterisk plays is 16-bit 8 kHz mono PCM WAV (D55).
        /// </summary>
        public const string AudioExtension = ".wav";

        /// <summary>
        /// The longest a derived file name may be, before the extension. Long enough for a real
        /// announcement name, short enough to stay comfortably inside any file system's limit.
        /// </summary>
        public const int MaxFileNameLength = 48;

        /// <summary>What a name with no letters or digits in it falls back to.</summary>
        private const string FallbackFileName = "announcement";

        public long AnnouncementID { get; set; }

        /// <summary>
        /// The file this announcement's audio is actually stored as, e.g. "main-greeting.wav", or
        /// empty when no audio has been uploaded yet. Derived from <see cref="Name"/> when the
        /// audio is saved, never from anything the browser sent (D55).
        /// </summary>
        public string AudioFile { get; set; } = "";

        /// <summary>What the announcement says, in an admin's words. Ends up as a dialplan comment.</summary>
        public string Description { get; set; } = "";

        public bool Enabled { get; set; } = true;

        /// <summary>What the announcement is called. Unique, and what the stored file is named after.</summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// The number a user can dial to hear the announcement, or empty for none. Optional
        /// because an announcement can exist purely to be a destination target later; without it
        /// there is nothing to Goto, so it cannot be picked as a destination either (D56).
        /// </summary>
        public string PlayExtension { get; set; } = "";

        /// <summary>The stored file without its extension, which is how Playback() names it.</summary>
        public string AudioBaseName =>
            this.AudioFile.EndsWith(AudioExtension, StringComparison.Ordinal)
                ? this.AudioFile[..^AudioExtension.Length]
                : this.AudioFile;

        /// <summary>Whether there is audio to play. An announcement without it is not rendered.</summary>
        public bool HasAudio => this.AudioFile.Length > 0;

        /// <summary>Whether a call can be sent here: something to play, and a number to send it to.</summary>
        public bool IsPlayable => this.Enabled && this.HasAudio && this.PlayExtension.Length > 0;

        /// <summary>
        /// The file name this announcement's audio should be stored as, derived from the name.
        /// Never from the uploaded file name, which is attacker-controlled, and never containing
        /// anything that would need escaping in a path or in a Playback() argument.
        /// </summary>
        public static string FileNameFor(string name) => SlugFor(name) + AudioExtension;

        /// <summary>
        /// Whether a stored file name is one we could have written: lower-case letters, digits and
        /// dashes, then ".wav". The store checks this again before it touches a path, so a row
        /// edited by hand cannot walk out of the sounds directory.
        /// </summary>
        public static bool IsValidAudioFile(string fileName) => AudioFilePattern().IsMatch(fileName);

        /// <summary>
        /// "Main Greeting (2026)" becomes "main-greeting-2026". Letters and digits are kept,
        /// everything else becomes a dash, runs of dashes collapse, and the result is trimmed and
        /// truncated. A name with nothing usable in it falls back to a fixed word, so there is
        /// always a file name.
        /// </summary>
        public static string SlugFor(string name)
        {
            var sb = new StringBuilder();

            foreach (var c in name)
            {
                if (char.IsAsciiLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
                else if (sb.Length > 0 && sb[^1] != '-')
                    sb.Append('-');
            }

            var slug = sb.ToString().Trim('-');
            if (slug.Length > MaxFileNameLength)
                slug = slug[..MaxFileNameLength].Trim('-');

            return slug.Length == 0 ? FallbackFileName : slug;
        }

        /// <summary>The announcement as a destination, for the catalog and the dialplan (D56).</summary>
        public Destination ToDestination() => new(DestinationType.Announcement, this.PlayExtension);

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

            // The same rule as an extension number, because it is dialled the same way and shares
            // the same context. Feature codes all start with *, so digits cannot collide with one;
            // collisions with a real extension or a ring group are checked by the repository,
            // which can see them.
            if (PlayExtension.Length > 0 && !Extension.IsValidNumber(PlayExtension))
                errors.Add("Play extension must be 2 to 6 digits, or blank for none.");

            if (AudioFile.Length > 0 && !IsValidAudioFile(AudioFile))
                errors.Add("The stored audio file name is not one this system would have written.");

            return errors;
        }

        [GeneratedRegex(@"^[a-z0-9][a-z0-9\-]{0,47}\.wav\z")]
        private static partial Regex AudioFilePattern();

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex DescriptionPattern();

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex NamePattern();
    }
}
