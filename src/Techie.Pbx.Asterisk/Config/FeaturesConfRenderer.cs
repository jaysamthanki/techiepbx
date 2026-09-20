using System.Text;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders features.conf: the DTMF a user can press during a call. There is exactly one entry
    /// in it — <c>parkcall</c>, the park feature code (D119) — because every other feature in this
    /// file is one we have not agreed to offer, and an unmapped feature is a feature nobody can
    /// trigger.
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

        public static string Render() => Render(new ParkingSettings());

        public static string Render(ParkingSettings parking)
        {
            parking.ThrowIfInvalid();

            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[featuremap]\n");

            if (!parking.Enabled)
            {
                sb.Append("; Nothing is mapped: call parking is switched off, and it is the only\n");
                sb.Append("; mid-call feature code this system offers (D119).\n");
                return sb.ToString();
            }

            sb.Append("; Park the call you are on. The Dial() options k and K are what let a channel\n");
            sb.Append("; use this at all; res_parking.conf decides where the call then goes (D119).\n");
            sb.Append($"{ParkFeature} => {ConfText.Safe(parking.DtmfCode, "park feature code")}\n");

            return sb.ToString();
        }
    }
}
