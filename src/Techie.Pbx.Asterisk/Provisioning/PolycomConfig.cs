using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// Everything <see cref="PolycomConfigRenderer"/> needs to write one phone's configuration:
    /// the row, the extension it is linked to (or null), and the three things that come from the
    /// server rather than from either — where to find us, where to ask the time, and what the
    /// time means here.
    ///
    /// Data in, text out: the renderer does no lookups, so the same inputs always produce the same
    /// file and a golden test can pin it (D79).
    /// </summary>
    public class PolycomConfig
    {
        /// <summary>
        /// The extension this phone registers as, or null when there is none to register — either
        /// nobody has assigned one yet, or the one assigned has since been switched off and so has
        /// no PJSIP endpoint to register against.
        /// </summary>
        public Extension? Extension { get; set; }

        /// <summary>Seconds east of UTC, which is what the phone's clock is set from.</summary>
        public int GmtOffsetSeconds { get; set; }

        public Phone Phone { get; set; } = new();

        /// <summary>The address the phone should register SIP to.</summary>
        public string ServerAddress { get; set; } = "";

        public int SipPort { get; set; } = PjsipTransport.DefaultPort;

        /// <summary>The address the phone should ask for the time, normally this server.</summary>
        public string SntpAddress { get; set; } = "";

        /// <summary>
        /// The offset the phone should apply to UTC, as Polycom wants it: a number of seconds,
        /// with no notion of a zone and so no notion of daylight saving. It is worked out from the
        /// System.Timezone setting <em>at the moment the file is generated</em>, which is the
        /// consequence worth knowing: a phone crosses a DST boundary when it next polls for its
        /// config, not when the clocks change (D82).
        ///
        /// A zone this machine cannot resolve falls back to UTC rather than throwing, for the same
        /// reason a bad port does elsewhere: a bad setting must not make provisioning fail, and the
        /// settings page is what reports it.
        /// </summary>
        public static int GmtOffsetFor(string timezone)
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                return (int)zone.GetUtcOffset(DateTimeOffset.UtcNow).TotalSeconds;
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return 0;
            }
        }
    }
}
