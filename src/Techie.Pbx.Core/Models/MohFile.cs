using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// One music on hold track. The audio lives on disk in its class's directory and is converted
    /// on upload exactly as an announcement's is (D55, D119); what is stored here is what the track
    /// is called and what its file is called, so that renaming a track and losing track of its file
    /// stay two different problems.
    ///
    /// There is no per-track anything else: a class is a directory, and Asterisk plays what it
    /// finds there in name order (D122). A track is not a destination and nothing points at it.
    /// </summary>
    public partial class MohFile
    {
        /// <summary>
        /// The extension every stored track gets. One format in, one format out, the same bargain
        /// an announcement makes: whatever was uploaded, what Asterisk plays is 16 kHz mono
        /// G.722, which <c>format_g722</c> reads and a wideband call plays with no transcoding
        /// at all (D122, after D117 made the voice path wideband).
        /// </summary>
        public const string AudioExtension = ".g722";

        /// <summary>
        /// The longest the derived part of a file name may be, before the ID prefix and the
        /// extension. The same limit an announcement's file has, for the same reason.
        /// </summary>
        public const int MaxFileNameLength = 48;

        /// <summary>What a name with no letters or digits in it falls back to.</summary>
        private const string FallbackFileName = "music";

        /// <summary>When the track was uploaded, as Unix seconds. Shown in the table, nothing else.</summary>
        public long CreatedUnix { get; set; }

        /// <summary>
        /// What the audio is stored as under the class's directory, e.g. "3-piano-loop.g722", or
        /// empty when nothing has been uploaded yet. Derived from <see cref="MohFileID"/> and
        /// <see cref="Name"/> when the audio is saved, never from anything the browser sent (D55).
        ///
        /// The ID is in the name because a class's directory is flat: two tracks called "Jazz" and
        /// "jazz!" would otherwise derive the same file and one would overwrite the other. It is
        /// not what makes a name valid, though — the tracks the installer puts in the class that
        /// ships are called "default-1.g722" and were never given an ID by anyone (D122).
        /// </summary>
        public string File { get; set; } = "";

        /// <summary>Whether there is audio on the row. A track without it has nothing to play.</summary>
        public bool HasAudio => this.File.Length > 0;

        /// <summary>The class this track belongs to, which is the directory it is played from (D122).</summary>
        public long MohClassID { get; set; }

        public long MohFileID { get; set; }

        /// <summary>What the track is called, in an admin's words. What the stored file is named after.</summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// The file name this track's audio should be stored as. Never the uploaded file name,
        /// which is attacker controlled, and never containing anything that would need escaping in
        /// a path or in a conf file.
        /// </summary>
        public static string FileNameFor(long mohFileID, string name) =>
            $"{mohFileID.ToString(CultureInfo.InvariantCulture)}-{SlugFor(name)}{AudioExtension}";

        /// <summary>
        /// Whether a stored file name is one this system could have written: lower-case letters,
        /// digits and dashes, then ".g722". No dot but the extension and no separator of any kind,
        /// so nothing built from it can leave the directory — the store checks it again before it
        /// touches a path.
        ///
        /// Uploads are always "&lt;id&gt;-&lt;slug&gt;.g722", but the ID prefix is not part of the
        /// rule: the shipped tracks are installed as "default-1.g722" by a script that has never
        /// seen the database (D122).
        /// </summary>
        public static bool IsValidFile(string fileName) => FilePattern().IsMatch(fileName);

        /// <summary>
        /// "Piano Loop (2026)" becomes "piano-loop-2026". The same rule an announcement's file name
        /// follows, written out here rather than shared because the two models have no reason to
        /// depend on each other.
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

        /// <summary>Returns a list of problems; empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(this.Name))
                errors.Add("Name is required.");
            else if (this.Name.Length > 64)
                errors.Add("Name must be 64 characters or fewer.");
            else if (!NamePattern().IsMatch(this.Name))
                errors.Add("Name may only contain letters, digits, spaces and . , ' - _ ( ) &");

            if (this.MohClassID <= 0)
                errors.Add("A music on hold track has to belong to a class.");

            if (this.File.Length > 0 && !IsValidFile(this.File))
                errors.Add("The stored file name is not one this system would have written.");

            return errors;
        }

        [GeneratedRegex(@"^[a-z0-9][a-z0-9\-]{0,63}\.g722\z")]
        private static partial Regex FilePattern();

        [GeneratedRegex(@"^[\p{L}\p{N} .,'\-_()&]+\z")]
        private static partial Regex NamePattern();
    }
}
