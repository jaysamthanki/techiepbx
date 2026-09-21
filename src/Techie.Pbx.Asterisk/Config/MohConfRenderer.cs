using System.Text;
using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders musiconhold.conf: one section per music on hold class, each naming its own
    /// directory (D122). D119 wrote exactly one class, because the only thing that played hold
    /// music was a parked call; a class is a directory as far as <c>res_musiconhold</c> is
    /// concerned, so several of them cost one section each and let a parked caller hear something
    /// different from everybody else.
    ///
    /// No class is ever called <c>default</c>, and that is load-bearing rather than taste.
    /// Asterisk falls back to a class called <c>default</c> whenever a caller asks for music and no
    /// class was named, so a class by that name would be played to a parked caller whose setting
    /// says silence. The name is refused by <see cref="MohClass.Validate"/> and again here, with
    /// the comparison done without regard to case because that is how Asterisk matches class names
    /// (res/res_musiconhold.c, <c>moh_class_cmp</c>). Naming nothing <c>default</c> means "no class
    /// named" really does mean nothing to play (see <see cref="ParkingConfRenderer"/>).
    ///
    /// <c>mode = files</c> reads the directory, so what Asterisk plays is whatever is in it rather
    /// than a list this file could enumerate — Asterisk has no per-file option in this mode. A
    /// class with no rows at all is still written out, because the tracks the installer puts in the
    /// class that ships need no database row to play. The rows there are are written as comments,
    /// so a directory that has drifted from the database can be spotted by reading the generated
    /// file next to an <c>ls</c>.
    /// </summary>
    public static class MohConfRenderer
    {
        public static string Render() => Render(new List<MohClass>(), new List<MohFile>());

        public static string Render(IEnumerable<MohClass> classes) => Render(classes, new List<MohFile>());

        public static string Render(IEnumerable<MohClass> classes, IEnumerable<MohFile> files)
        {
            var all = RenderOrder(classes);
            var tracks = RenderOrder(files);

            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[general]\n");
            // The lot's parkedmusicclass has to win. With this on — Asterisk's default — a class
            // suggested by the channel driver would beat it, and a PJSIP endpoint suggests
            // "default" without being asked.
            sb.Append("preferchannelclass = no\n");

            foreach (var mohClass in all)
            {
                sb.Append('\n');
                sb.Append($"[{ConfText.Safe(mohClass.Name, "music on hold class")}]\n");
                sb.Append("mode = files\n");
                sb.Append($"directory = {ConfText.Safe(MohStore.ConfDirectory(mohClass), "music on hold directory")}\n");

                // Named order, so the tracks play in the order the Music on hold table lists them
                // rather than in whatever order the file system hands the directory over in.
                sb.Append("sort = alpha\n");

                var mine = tracks.Where(t => t.MohClassID == mohClass.MohClassID).ToList();
                if (mine.Count == 0)
                    continue;

                sb.Append("; The tracks the database expects to find in that directory, in the order they\n");
                sb.Append("; will play. Asterisk reads the directory itself: this list is here to be read\n");
                sb.Append("; next to it, not obeyed.\n");

                foreach (var track in mine)
                    sb.Append($"; {ConfText.Safe(track.File, "music on hold file")} - {ConfText.Safe(track.Name, "music on hold name")}\n");
            }

            return sb.ToString();
        }

        /// <summary>
        /// The classes that are worth writing out, in the order they are written: the one that
        /// ships first, then by name. Re-validated, so a row that reached the database another way
        /// cannot reach a conf file, and two classes whose names differ only in case are refused —
        /// Asterisk would treat them as one and play whichever it loaded last.
        /// </summary>
        public static List<MohClass> RenderOrder(IEnumerable<MohClass> classes)
        {
            var all = classes.ToList();

            foreach (var mohClass in all)
            {
                var errors = mohClass.Validate();
                if (errors.Count > 0)
                    throw new InvalidOperationException($"Music on hold class '{mohClass.Name}' is invalid: {string.Join(" ", errors)}");
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mohClass in all)
            {
                if (!names.Add(mohClass.Name.Trim()))
                    throw new InvalidOperationException($"There is more than one music on hold class called '{mohClass.Name}'.");
            }

            return all
                .OrderByDescending(c => c.IsDefault)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The tracks that are worth writing about, in the order Asterisk will play them, which is
        /// by file name because a class sorts its directory alphabetically. Re-validated, so a row
        /// that reached the database another way cannot reach a conf file; a row with no audio is
        /// left out, because there is nothing on disk for it.
        /// </summary>
        public static List<MohFile> RenderOrder(IEnumerable<MohFile> files)
        {
            var all = files.ToList();

            foreach (var file in all)
            {
                var errors = file.Validate();
                if (errors.Count > 0)
                    throw new InvalidOperationException($"Music on hold track '{file.Name}' is invalid: {string.Join(" ", errors)}");
            }

            return all
                .Where(f => f.HasAudio)
                .OrderBy(f => f.File, StringComparer.Ordinal)
                .ToList();
        }
    }
}
