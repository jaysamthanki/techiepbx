using System.Text;
using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// One music on hold class: a name Asterisk knows it by and a directory it plays (D122). A
    /// class is the unit res_musiconhold actually has — <c>mode = files</c> plays a directory — so
    /// having several of them is how a parked caller hears something different from anyone else.
    ///
    /// <see cref="Name"/> is written into the generated musiconhold.conf as the section heading and
    /// into res_parking.conf as <c>parkedmusicclass</c>, so it is what Asterisk matches on.
    /// <see cref="Directory"/> is one subdirectory of the music on hold path and never leaves this
    /// system. They are two fields rather than one because renaming a class should not have to move
    /// files, and because a name an admin would write ("Front desk") is not a path.
    /// </summary>
    public partial class MohClass
    {
        /// <summary>The directory the class that ships with the product plays, under the base path.</summary>
        public const string DefaultDirectory = "default";

        /// <summary>
        /// What the class that ships with the product is called. Deliberately not "Default", which
        /// is <see cref="ReservedName"/>: the table says which class is the default one with a
        /// badge, and the one name Asterisk will not share is worth more than the word.
        /// </summary>
        public const string DefaultName = "Standard";

        /// <summary>How long a directory name may be. Enough to say what it is, short enough to read.</summary>
        public const int MaxDirectoryLength = 24;

        public const int MaxNameLength = 32;

        /// <summary>
        /// The class name Asterisk reserves. <c>local_ast_moh_start</c> falls back to a class called
        /// "default" whenever music is asked for and none was named, so a class by that name would
        /// be played to a parked caller whose setting says silence — which is exactly how silence is
        /// implemented here (D119). Rejected case-insensitively, because Asterisk compares class
        /// names with <c>strcasecmp</c>.
        /// </summary>
        public const string ReservedName = "default";

        /// <summary>What a name with no letters or digits in it falls back to as a directory.</summary>
        private const string FallbackDirectory = "music";

        /// <summary>
        /// The subdirectory of the music on hold path this class plays, e.g. "default" or
        /// "front-desk". Lower-case letters, digits and dashes only: it is built into a path and
        /// written into a conf file, so it never contains anything that would need escaping.
        /// </summary>
        public string Directory { get; set; } = "";

        /// <summary>
        /// Whether this is the class that ships with the product. It cannot be deleted: its
        /// directory is what the installer puts the shipped tracks into.
        /// </summary>
        public bool IsDefault { get; set; }

        public long MohClassID { get; set; }

        /// <summary>
        /// What Asterisk calls this class: the section heading in musiconhold.conf, and the value
        /// anything that wants this music names. Unique without regard to case, because that is how
        /// Asterisk matches it.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// "Front Desk" becomes "front-desk": what the form offers as a directory when an admin has
        /// only typed a name. Only a suggestion — the directory is stored as its own field and is
        /// not re-derived when the class is renamed, because the files are already in it.
        /// </summary>
        public static string DirectoryFor(string name)
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
            if (slug.Length > MaxDirectoryLength)
                slug = slug[..MaxDirectoryLength].Trim('-');

            return slug.Length == 0 ? FallbackDirectory : slug;
        }

        /// <summary>Whether this name is the one Asterisk reserves for its own fallback class.</summary>
        public static bool IsReservedName(string name) =>
            string.Equals(name.Trim(), ReservedName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether a directory name is one this system would have written. Checked again by the
        /// store before a path is built from it, so a row edited by hand cannot walk out of the
        /// music on hold directory.
        /// </summary>
        public static bool IsValidDirectory(string directory) => DirectoryPattern().IsMatch(directory);

        /// <summary>
        /// Whether a class name is one this system would write into a conf file. Public because a
        /// setting that names a class — the parking one (D122) — is checked the same way before it
        /// is stored, and a name is checked once more by the renderer before it is written.
        /// </summary>
        public static bool IsValidName(string name)
        {
            var trimmed = name.Trim();

            return trimmed.Length is > 0 and <= MaxNameLength
                && NamePattern().IsMatch(trimmed)
                && !IsReservedName(trimmed);
        }

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(this.Name))
                errors.Add("Name is required.");
            else if (this.Name.Length > MaxNameLength)
                errors.Add($"Name must be {MaxNameLength} characters or fewer.");
            else if (!NamePattern().IsMatch(this.Name))
                errors.Add("Name may only contain letters, digits, spaces, dashes and underscores.");
            else if (IsReservedName(this.Name))
                errors.Add("'default' is the class name Asterisk keeps for itself: it is played whenever " +
                           "music is asked for and no class was named, which is how a parked caller gets " +
                           "silence here. Call it something else.");

            if (string.IsNullOrWhiteSpace(this.Directory))
                errors.Add("Directory is required.");
            else if (!IsValidDirectory(this.Directory))
                errors.Add($"Directory may only contain lower-case letters, digits and dashes, and must be " +
                           $"{MaxDirectoryLength} characters or fewer.");

            return errors;
        }

        [GeneratedRegex(@"^[a-z0-9][a-z0-9\-]{0,23}$")]
        private static partial Regex DirectoryPattern();

        [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} \-_]*$")]
        private static partial Regex NamePattern();
    }
}
