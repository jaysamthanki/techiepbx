using System.Globalization;
using System.Text;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders res_parking.conf, the one parking lot this system has (D119). The file is
    /// <b>res_parking.conf</b> and not parking.conf: parking moved out of features.conf into its
    /// own module in Asterisk 12, and the sample that ships with Asterisk 22 is
    /// <c>configs/samples/res_parking.conf.sample</c>.
    ///
    /// Two things this file deliberately does not do.
    ///
    /// It does not let the lot's own context be dialled from ours. The slot extensions are
    /// written in <see cref="ExtensionsConfRenderer"/>, one at a time, like every other number
    /// here, because only numbers we generated can be dialled (D12, D46, D60). The
    /// <c>parkext</c> below is not reachable from a phone: it lives in the lot's private
    /// context, and it is there because the DTMF feature parks by blind-transferring the peer
    /// into that extension — with no parkext the feature hook fires and nothing happens,
    /// which the lab found the hard way (D119).
    ///
    /// And when the parked caller is meant to hear silence, it writes no <c>parkedmusicclass</c>
    /// at all. Asterisk's parking has no "silence" option — a parked channel joins a holding
    /// bridge whose idle mode is always music on hold — so silence is what happens when there is
    /// no class to start: bridge_holding falls back to a silence generator when ast_moh_start
    /// fails. The generated musiconhold.conf never defines a class called <c>default</c> for
    /// exactly this reason, so the fallback chain finds nothing and the caller hears nothing.
    /// Which class it names when there is music is the Parking.MusicClass setting: parking is one
    /// of several classes now, not the only one (D122).
    /// </summary>
    public static class ParkingConfRenderer
    {
        /// <summary>
        /// The lot's dialplan context. Its only extension is <see cref="ParkExt"/>, which no
        /// phone can dial; it is written so that an admin reading the file is not left wondering
        /// what the default would have been.
        /// </summary>
        public const string Context = "parkedcalls";

        /// <summary>
        /// Where the park feature transfers the parked channel into the lot. It exists only in
        /// <see cref="Context"/>, so it cannot collide with an extension, and no phone dials it:
        /// retrieval is the slot numbers written into the dialplan (D119).
        /// </summary>
        public const string ParkExt = "700";

        /// <summary>
        /// The lot's name, which is also its section heading. Asterisk guarantees a lot called
        /// <c>default</c> exists whether the file mentions it or not, so using that name means
        /// there is one lot rather than one we configure and one Asterisk invents.
        /// </summary>
        public const string LotName = "default";

        /// <summary>
        /// How long the call rings the phone that parked it when the park times out. Asterisk's
        /// own default, written down because it is the second half of the timeout an admin sets:
        /// the call is theirs again 30 seconds after it comes back, answered or not.
        /// </summary>
        private const int ComebackDialSeconds = 30;

        public static string Render() => Render(new ParkingSettings());

        public static string Render(ParkingSettings parking)
        {
            parking.ThrowIfInvalid();

            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[general]\n");
            // A dialplan that can invent parking lots is a dialplan that can invent extensions.
            sb.Append("parkeddynamic = no\n");

            if (!parking.Enabled)
            {
                sb.Append('\n');
                sb.Append("; Call parking is switched off, so no lot is configured here. Asterisk still\n");
                sb.Append("; creates a lot called 'default' by itself, but nothing reaches it: no park\n");
                sb.Append("; feature code in features.conf and no slot extensions in the dialplan (D119).\n");
                return sb.ToString();
            }

            var slots = parking.Slots.ToString(CultureInfo.InvariantCulture);

            sb.Append('\n');
            sb.Append($"[{LotName}]\n");
            sb.Append($"; Slots 1 to {slots}. Slot extensions are written into the generated\n");
            sb.Append("; dialplan one at a time, like every other number here; the parkext is only\n");
            sb.Append("; what the DTMF feature blind-transfers the parked channel into (D119).\n");
            sb.Append($"context => {Context}\n");
            sb.Append($"parkext => {ParkExt}\n");
            sb.Append($"parkpos => 1-{slots}\n");

            // 'first' rather than 'next': a caller who parks two calls in a row expects slot 1 and
            // then slot 2, not slot 1 and then whatever came after the last one used.
            sb.Append("findslot => first\n");

            sb.Append($"parkingtime => {parking.TimeoutSeconds.ToString(CultureInfo.InvariantCulture)}\n");

            // The call comes back to the phone that parked it, which is the only answer that needs
            // no destination picker: whoever parked it is who forgot about it (D119).
            sb.Append("comebacktoorigin = yes\n");
            sb.Append($"comebackdialtime = {ComebackDialSeconds.ToString(CultureInfo.InvariantCulture)}\n");

            if (parking.UsesMusicOnHold)
            {
                sb.Append('\n');
                sb.Append("; What the parked caller hears: one of the classes the generated\n");
                sb.Append("; musiconhold.conf defines, chosen by the Parking.MusicClass setting (D122).\n");
                sb.Append($"parkedmusicclass = {ConfText.Safe(parking.MusicClass.Trim(), "music on hold class")}\n");
            }
            else
            {
                sb.Append('\n');
                sb.Append("; No parkedmusicclass, which is how a parked caller gets silence: Asterisk's\n");
                sb.Append("; holding bridge always tries music on hold first, and falls back to a silence\n");
                sb.Append("; generator when there is no class to start. The generated musiconhold.conf\n");
                sb.Append("; never defines a class called 'default', so there is nothing to find (D119).\n");
            }

            return sb.ToString();
        }
    }
}
