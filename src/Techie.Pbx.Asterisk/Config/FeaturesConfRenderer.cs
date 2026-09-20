using System.Text;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders features.conf: the DTMF a user can press during a call. The entries are
    /// <c>atxfer</c> (attended transfer, always) and, when parking is on, <c>parkcall</c> — every
    /// other feature in this file is one we have not agreed to offer, and an unmapped feature is
    /// a feature nobody can trigger.
    ///
    /// Two things have to line up for it to work, and neither is in this file. The featuremap is
    /// only consulted for a channel whose Dial() asked for it, which is why every generated Dial
    /// carries <c>k</c> and <c>K</c> (see <see cref="ExtensionsConfRenderer.DialOptions"/>); and
    /// the park itself is res_parking's, which is configured in res_parking.conf.
    ///
    /// Worth knowing, because it is not written here: Asterisk's own default for
    /// <c>blindxfer</c> is <c>#</c>, and the <c>t</c>/<c>T</c> in those same Dial options turn it
    /// on. Pressing # mid-call therefore starts a blind transfer, which is the intended behaviour
    /// (D119) but is not something this file says out loud.
    /// </summary>
    public static class FeaturesConfRenderer
    {
        /// <summary>
        /// The featuremap entry res_parking registers itself against. Asterisk's own name for it —
        /// "park" is the application, "parkcall" is the feature — and its built-in default is
        /// empty, so a file without this line is a system where no DTMF parks anything.
        /// </summary>
        public const string ParkFeature = "parkcall";

        /// <summary>
        /// Attended transfer, in the DTMF form a phone without a transfer button needs. Asterisk's
        /// own default is empty — nothing triggers it unless it is written here. *2 is the
        /// convention FreePBX users already know, and star-prefixed codes are this system's rule.
        /// The blind form (#) needs no entry: it is Asterisk's default and tT turns it on.
        /// </summary>
        public const string AttendedTransferFeature = "atxfer";

        /// <summary>The DTMF a user presses to start an attended transfer (D119).</summary>
        public const string AttendedTransferCode = "*2";

        /// <summary>
        /// The DTMF a user presses to start a blind transfer. Written down here and <b>not</b>
        /// written into the file: it is Asterisk's own built-in default for <c>blindxfer</c>, and
        /// what turns it on is the <c>t</c>/<c>T</c> in every generated Dial (D119). It is a
        /// constant so that the cheat sheet can print the code users actually have without
        /// inventing it a second time (D120).
        /// </summary>
        public const string BlindTransferCode = "#";

        public static string Render() => Render(new ParkingSettings());

        public static string Render(ParkingSettings parking)
        {
            parking.ThrowIfInvalid();

            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[featuremap]\n");

            sb.Append("; Attended transfer: press the code, dial the target, then complete the\n");
            sb.Append("; transfer. The t/T Dial() options are what let a channel use this at all (D119).\n");
            sb.Append($"{AttendedTransferFeature} => {ConfText.Safe(AttendedTransferCode, "attended transfer code")}\n");

            if (!parking.Enabled)
            {
                sb.Append("; Nothing further: call parking is switched off, so the park feature\n");
                sb.Append("; code below stays out of the file (D119).\n");
                return sb.ToString();
            }

            sb.Append("; Park the call you are on. The Dial() options k and K are what let a channel\n");
            sb.Append("; use this at all; res_parking.conf decides where the call then goes (D119).\n");
            sb.Append($"{ParkFeature} => {ConfText.Safe(parking.DtmfCode, "park feature code")}\n");

            return sb.ToString();
        }
    }
}
