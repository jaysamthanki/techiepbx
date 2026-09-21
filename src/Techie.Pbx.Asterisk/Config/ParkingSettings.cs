using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Call parking as the generated config sees it (D119): whether it exists, how many slots it
    /// has, how long a call sits in one, the DTMF that puts it there, and what the parked caller
    /// hears. Three generated files read it — features.conf, res_parking.conf and the dialplan's
    /// slot routes — so it is one object rather than three reads of the Settings table.
    /// musiconhold.conf is not one of them: the classes are the Music on hold page's (D122), and it
    /// is res_parking.conf that decides whether a parked caller is pointed at one, and at which.
    ///
    /// Built the way <see cref="PjsipTransport"/> is: plain properties with the defaults on them,
    /// filled in from the Settings table by <see cref="AsteriskSettings.Parking"/>, and re-checked
    /// by the renderers before anything is written.
    /// </summary>
    public class ParkingSettings
    {
        /// <summary>The DTMF a user presses to park a call when nobody has chosen one.</summary>
        public const string DefaultDtmfCode = "*3";

        /// <summary>
        /// The music on hold class a parked caller hears when nobody has chosen one: the class that
        /// ships with the product (D122). A name rather than an ID, because that is what Asterisk
        /// matches and what the generated <c>parkedmusicclass</c> has to say.
        /// </summary>
        public const string DefaultMusicClass = MohClass.DefaultName;

        /// <summary>
        /// Nine slots, which is every single digit there is. Generous rather than minimal because
        /// an unused slot costs nothing: it is one more extension in the generated dialplan.
        /// </summary>
        public const int DefaultSlots = SettingsValidation.MaxParkingSlots;

        /// <summary>
        /// A minute before a parked call comes back to whoever parked it. Long enough to walk to
        /// the other phone, short enough that a forgotten call is noticed rather than dropped.
        /// </summary>
        public const int DefaultTimeoutSeconds = 60;

        /// <summary>
        /// What the parked caller hears: a <see cref="ParkingAudio"/> value. Silence by default,
        /// which is what happens anyway until somebody uploads a track.
        /// </summary>
        public string Audio { get; set; } = ParkingAudio.Silence;

        /// <summary>The <c>parkcall</c> entry of features.conf. A star and one or two digits.</summary>
        public string DtmfCode { get; set; } = DefaultDtmfCode;

        /// <summary>
        /// Whether any of this is generated at all. Off means an empty res_parking.conf, no
        /// parkcall in features.conf and no slot routes in the dialplan — the modules are still
        /// loaded, because modules.conf is not conditional, but nothing can reach them.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Which music on hold class a parked caller hears, when <see cref="Audio"/> says music at
        /// all (D122). The class's name, written out as <c>parkedmusicclass</c>; a name that
        /// matches no class means Asterisk finds nothing to start, which is the same silence as
        /// having asked for none.
        /// </summary>
        public string MusicClass { get; set; } = DefaultMusicClass;

        /// <summary>
        /// The slot numbers, 1..<see cref="Slots"/>, which is what the dialplan writes an entry
        /// each for. Empty when parking is off.
        /// </summary>
        public IEnumerable<int> SlotNumbers => this.Enabled
            ? Enumerable.Range(1, this.Slots)
            : Enumerable.Empty<int>();

        /// <summary>How many slots there are: 1 to 9, one digit each, dialled to retrieve.</summary>
        public int Slots { get; set; } = DefaultSlots;

        /// <summary>res_parking.conf's parkingtime: how long a call stays parked, in seconds.</summary>
        public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

        /// <summary>
        /// Whether a parked caller gets the generated music on hold class rather than silence.
        /// Parking being off makes this false however the audio setting reads: there is nothing to
        /// park, so there is nothing to play to.
        /// </summary>
        public bool UsesMusicOnHold => this.Enabled && ParkingAudio.IsMusicOnHold(this.Audio);

        /// <summary>Returns a list of problems; empty means these can be written out.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (!ParkingAudio.IsKnown(this.Audio))
                errors.Add($"Parking audio must be one of: {string.Join(", ", ParkingAudio.All)}.");

            if (!SettingsValidation.IsParkingDtmfCode(this.DtmfCode))
                errors.Add("The park feature code must be a star and one or two digits, e.g. *3.");

            if (!MohClass.IsValidName(this.MusicClass))
                errors.Add("The music on hold class a parked caller hears must be the name of a class on the " +
                           "Music on hold page, and cannot be 'default'.");

            if (this.Slots is < SettingsValidation.MinParkingSlots or > SettingsValidation.MaxParkingSlots)
                errors.Add($"Parking slots must be between {SettingsValidation.MinParkingSlots} and {SettingsValidation.MaxParkingSlots}.");

            if (this.TimeoutSeconds is < SettingsValidation.MinParkingTimeoutSeconds or > SettingsValidation.MaxParkingTimeoutSeconds)
                errors.Add($"The parking timeout must be between {SettingsValidation.MinParkingTimeoutSeconds} and {SettingsValidation.MaxParkingTimeoutSeconds} seconds.");

            return errors;
        }

        /// <summary>
        /// Throws when these settings could not be written out. Called by every renderer that
        /// reads them, the way the dialplan renderer re-validates every row it is given: a value
        /// that reached this object some other way must not reach a conf file.
        /// </summary>
        public void ThrowIfInvalid()
        {
            var errors = this.Validate();

            if (errors.Count > 0)
                throw new InvalidOperationException($"Parking settings are invalid: {string.Join(" ", errors)}");
        }
    }
}
