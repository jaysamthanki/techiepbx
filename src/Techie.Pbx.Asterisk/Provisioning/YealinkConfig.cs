using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// Everything <see cref="YealinkConfigRenderer"/> needs to write one phone's configuration:
    /// the row, the extension it is linked to (or null), and the settings that come from the
    /// server rather than from either — where to find us, where to ask the time, what the time
    /// means here, and where to poll for config again.
    ///
    /// Data in, text out: the renderer does no lookups, so the same inputs always produce the same
    /// file and a golden test can pin it (D88, mirroring D79).
    /// </summary>
    public class YealinkConfig
    {
        /// <summary>The codecs to offer, in preference order, from <c>Sip.Codecs</c>.</summary>
        public List<string> Codecs { get; set; } = new();

        /// <summary>
        /// The extension this phone registers as, or null when there is none to register — either
        /// nobody has assigned one yet, or the one assigned has since been switched off and so has
        /// no PJSIP endpoint to register against.
        /// </summary>
        public Extension? Extension { get; set; }

        /// <summary>
        /// The address the phone should ask for the time: the <c>System.NtpServer</c> setting,
        /// or its default, a public pool (D84).
        /// </summary>
        public string NtpServer { get; set; } = "";

        public Phone Phone { get; set; } = new();

        /// <summary>The password the phone presents when it re-polls its provisioning URL.</summary>
        public string ProvisioningPassword { get; set; } = "";

        /// <summary>
        /// The full URL the phone should re-poll for its configuration — scheme, host and port,
        /// and the <c>/yealink</c> route. Unlike Polycom's option 160 URL, which the phone already
        /// carries, this is written into the file itself so a phone that was provisioned once
        /// keeps coming back on its own <c>auto_provision.repeat</c> schedule.
        /// </summary>
        public string ProvisioningUrl { get; set; } = "";

        /// <summary>The username the phone presents when it re-polls its provisioning URL.</summary>
        public string ProvisioningUsername { get; set; } = "";

        /// <summary>The address the phone should register SIP to.</summary>
        public string ServerAddress { get; set; } = "";

        public int SipPort { get; set; } = PjsipTransport.DefaultPort;

        /// <summary>
        /// The offset the phone's clock should apply, worked out at generation time (D82, D92):
        /// see <see cref="TimeZoneOffsetFor"/>.
        /// </summary>
        public string TimeZoneOffset { get; set; } = "0";

        /// <summary>
        /// The offset a Yealink phone's <c>local_time.time_zone</c> wants: hours east of UTC as
        /// Yealink's own parameter table expects it ("-8", "0", "+5.5", ...), not the seconds
        /// Polycom takes (D82) — Yealink's own admin guide describes this parameter in
        /// whole-or-half hours, with an explicit "+" for a positive offset and no sign at all for
        /// zero. Worked out at the moment the file is generated, so the same caveat as Polycom's
        /// D82 applies: a phone crosses a DST boundary at its next poll, not when the clocks
        /// change. A zone this machine cannot resolve falls back to "0" rather than throwing
        /// (mirrors D82's reasoning exactly). **Not verified against a real handset (D92).**
        /// </summary>
        public static string TimeZoneOffsetFor(string timezone)
        {
            double hours;
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                hours = zone.GetUtcOffset(DateTimeOffset.UtcNow).TotalHours;
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                hours = 0;
            }

            if (hours == 0)
                return "0";

            var sign = hours > 0 ? "+" : "-";
            return sign + Math.Abs(hours).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
