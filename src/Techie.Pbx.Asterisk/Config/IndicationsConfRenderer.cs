using System.Text;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders indications.conf: the tone zone Asterisk plays its own ringing, busy and
    /// congestion tones from (D132). Dial's <c>r</c> option (D127) on a caller an IVR or ring
    /// group has already answered makes the ring tone inband, from the channel's tone zone — and
    /// with no file there is no zone at all, so the caller hears silence where the ringing should
    /// be.
    ///
    /// One zone, <c>us</c>, copied verbatim from the stock Asterisk 22 sample, and nothing to
    /// choose: a country setting is a possible later knob, not part of this piece. The tone
    /// handling is built into the core as "indications", so there is no module to allow in
    /// modules.conf, and a reload of it re-reads this file.
    /// </summary>
    public static class IndicationsConfRenderer
    {
        public const string FileName = "indications.conf";

        /// <summary>The <c>us</c> zone of Asterisk 22's indications.conf.sample, line for line.</summary>
        private static readonly (string Key, string Value)[] UsZone =
        {
            ("description", "United States / North America"),
            ("ringcadence", "2000,4000"),
            ("dial", "350+440"),
            ("busy", "480+620/500,0/500"),
            ("ring", "440+480/2000,0/4000"),
            ("congestion", "480+620/250,0/250"),
            ("callwaiting", "440/300,0/10000"),
            ("dialrecall", "!350+440/100,!0/100,!350+440/100,!0/100,!350+440/100,!0/100,350+440"),
            ("record", "1400/500,0/15000"),
            ("info", "!950/330,!1400/330,!1800/330,0"),
            ("stutter", "!350+440/100,!0/100,!350+440/100,!0/100,!350+440/100,!0/100,!350+440/100,!0/100,!350+440/100,!0/100,!350+440/100,!0/100,350+440"),
        };

        public static string Render()
        {
            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[general]\n");
            sb.Append("country = us\n");

            sb.Append('\n');
            sb.Append("[us]\n");

            foreach (var (key, value) in UsZone)
                sb.Append($"{ConfText.Safe(key, "indication")} = {ConfText.Safe(value, key)}\n");

            return sb.ToString();
        }
    }
}
