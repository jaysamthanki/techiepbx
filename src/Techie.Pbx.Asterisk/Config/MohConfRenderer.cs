using System.Text;
using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders musiconhold.conf: one class, one directory, the tracks an admin uploaded (D119).
    /// There is no per-class UI and no second class, because the only thing in this system that
    /// plays music on hold is a parked call.
    ///
    /// The class is <b>not</b> called <c>default</c>, and that is load-bearing rather than taste.
    /// Asterisk falls back to a class called <c>default</c> whenever a caller asks for music and
    /// no class was named, so a class by that name would be played to a parked caller whose
    /// setting says silence. Naming it something else means "no class named" really does mean
    /// nothing to play (see <see cref="ParkingConfRenderer"/>).
    ///
    /// <c>mode = files</c> reads the directory, so what Asterisk plays is whatever is in it rather
    /// than a list this file could enumerate — Asterisk has no per-file option in this mode. The
    /// rows are written out as comments anyway, so a directory that has drifted from the database
    /// can be spotted by reading the generated file next to an <c>ls</c>.
    /// </summary>
    public static class MohConfRenderer
    {
        /// <summary>
        /// The one music on hold class. Deliberately not <c>default</c>: see the class summary.
        /// </summary>
        public const string ClassName = "parking";

        public static string Render() => Render(new List<MohFile>());

        public static string Render(IEnumerable<MohFile> files)
        {
            var tracks = RenderOrder(files);

            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[general]\n");
            // The lot's parkedmusicclass has to win. With this on — Asterisk's default — a class
            // suggested by the channel driver would beat it, and a PJSIP endpoint suggests
            // "default" without being asked.
            sb.Append("preferchannelclass = no\n");

            if (tracks.Count == 0)
            {
                sb.Append('\n');
                sb.Append("; No music on hold has been uploaded, so no class is defined. A parked caller\n");
                sb.Append("; hears silence whatever the parking audio setting says (D119).\n");
                return sb.ToString();
            }

            sb.Append('\n');
            sb.Append($"[{ClassName}]\n");
            sb.Append("mode = files\n");
            sb.Append($"directory = {ConfText.Safe(MohStore.DefaultMohPath, "music on hold directory")}\n");

            // Named order, so the tracks play in the order the Music on hold table lists them
            // rather than in whatever order the file system hands the directory over in.
            sb.Append("sort = alpha\n");

            sb.Append('\n');
            sb.Append("; The tracks the database expects to find in that directory, in the order they\n");
            sb.Append("; will play. Asterisk reads the directory itself: this list is here to be read\n");
            sb.Append("; next to it, not obeyed.\n");

            foreach (var track in tracks)
                sb.Append($"; {ConfText.Safe(track.File, "music on hold file")} - {ConfText.Safe(track.Name, "music on hold name")}\n");

            return sb.ToString();
        }

        /// <summary>
        /// The tracks that are worth writing about, in the order Asterisk will play them, which is
        /// by file name because the class sorts alphabetically. Re-validated, so a row that reached
        /// the database another way cannot reach a conf file; a row with no audio is left out,
        /// because there is nothing on disk for it.
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
