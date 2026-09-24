using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Provisioning
{
    /// <summary>
    /// Everything <see cref="PolycomConfigRenderer"/> needs to write one phone's configuration:
    /// the row, the keys on it, the extensions those keys name, and the things that come from the
    /// server rather than from any of them — where to find us, where to ask the time, what the
    /// time means here, and whether there is anywhere to park a call (D144).
    ///
    /// Data in, text out: the renderer does no lookups, so the same inputs always produce the same
    /// file and a golden test can pin it (D79).
    /// </summary>
    public class PolycomConfig
    {
        /// <summary>
        /// The Polycom web UI's local admin ("Polycom") account password, from
        /// <c>Provisioning.AdminPassword</c>, or empty when nobody has set one. Empty means the
        /// attribute is left out of the file rather than written as blank (D84).
        /// </summary>
        public string AdminPassword { get; set; } = "";

        /// <summary>
        /// The absolute URL of the site's background image, behind the same <c>/polycom</c> gate
        /// as this file (D145, D151), or empty when no image has been uploaded. Empty means the
        /// <c>bg</c> element is not written at all and the phone keeps its own background: the
        /// file is byte for byte what it was before the feature existed.
        /// </summary>
        public string BackgroundUrl { get; set; } = "";

        /// <summary>
        /// The assignable keys, already reduced to the ones that can work by
        /// <see cref="PhoneButton.Usable"/>: this renderer writes what it is given rather than
        /// deciding whether a target is still there. The line keys among them are what this phone
        /// registers as, in key order, and the rest are its lamps (D121, schema 020). No line keys
        /// means no registration, which is what an auto-added phone nobody has assigned yet gets.
        /// </summary>
        public List<PhoneButton> Buttons { get; set; } = new();

        /// <summary>
        /// The call flow controls a key may name, for the key's label: a switch is shown by its own
        /// name (F9). What the key subscribes to and dials is its code, already on the button.
        /// </summary>
        public List<CallFlowControl> CallFlowControls { get; set; } = new();

        /// <summary>
        /// The extensions the keys may name: where a line key's credentials come from and where a
        /// lamp's label does (D121). The whole list rather than one row, because a phone registers
        /// as one extension and watches several others.
        /// </summary>
        public List<Extension> Extensions { get; set; } = new();

        /// <summary>Seconds east of UTC, which is what the phone's clock is set from.</summary>
        public int GmtOffsetSeconds { get; set; }

        /// <summary>
        /// The DTMF that parks a call: <c>Parking.DtmfCode</c>, the <c>parkcall</c> entry of
        /// features.conf (D119). The Park soft key sends it mid-call rather than transferring to
        /// it, because it is a feature code and not a dialable extension (D144). Read only when
        /// <see cref="ParkEnabled"/> is set.
        /// </summary>
        public string ParkDtmfCode { get; set; } = ParkingSettings.DefaultDtmfCode;

        /// <summary>
        /// Whether call parking is on (<c>Parking.Enabled</c>). Off means no Park soft key at all:
        /// a key that sends a code Asterisk does nothing with is worse than no key (D144).
        /// </summary>
        public bool ParkEnabled { get; set; }

        public Phone Phone { get; set; } = new();

        /// <summary>The address the phone should register SIP to.</summary>
        public string ServerAddress { get; set; } = "";

        public int SipPort { get; set; } = PjsipTransport.DefaultPort;

        /// <summary>
        /// The address the phone should ask for the time: the <c>System.NtpServer</c> setting,
        /// or its default, a public pool (D84). Not this server itself — unlike the SIP server
        /// address, there is no reason TNPBX has to be the one answering.
        /// </summary>
        public string SntpAddress { get; set; } = "";

        /// <summary>
        /// The Polycom web UI's local user ("User") account password, from
        /// <c>Provisioning.UserPassword</c>, or empty when nobody has set one (D84).
        /// </summary>
        public string UserPassword { get; set; } = "";

        /// <summary>
        /// The offset the phone should apply to UTC, as Polycom wants it: a number of seconds. It
        /// is the zone's <em>standard</em> offset, not whatever is in force at generation time: a
        /// Polycom adds its own daylight-saving hour on top of the offset whatever the config says
        /// (D149's attempt to switch that rule off from the config did not hold on a real Edge
        /// E450), so the number must be the no-DST one and the phone's own rule supplies the summer
        /// hour — the production-proven shape, exactly how the FreePBX module configured Pacific
        /// sites at -28800 with no daylightSaving parameter at all (D150, amending D82).
        ///
        /// The standard offset is read from the zone's January and July offsets and taking the
        /// smaller: daylight saving always adds, in either hemisphere, so the smaller of the two is
        /// the standard one, and for a zone with no daylight saving the two are equal. The dates
        /// are fixed, so the answer does not drift with the season or the moment the file is
        /// generated — which also retires D82's caveat: the phone now crosses DST boundaries on
        /// the phone's own rule, at the moment the clocks change, not at its next config poll.
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
                var january = zone.GetUtcOffset(new DateTimeOffset(2020, 1, 15, 0, 0, 0, TimeSpan.Zero));
                var july = zone.GetUtcOffset(new DateTimeOffset(2020, 7, 15, 0, 0, 0, TimeSpan.Zero));
                var standard = january < july ? january : july;

                return (int)standard.TotalSeconds;
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return 0;
            }
        }
    }
}
