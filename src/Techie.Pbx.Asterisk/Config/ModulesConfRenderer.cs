using System.Text;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders modules.conf as an explicit allowlist: <c>autoload = no</c> and one
    /// <c>load =</c> line per module we actually use (D31). A stock Asterisk loads well over a
    /// hundred modules, most of which are channel drivers, protocols and applications this PBX
    /// will never call; every one of them is code reachable from the network.
    ///
    /// The list is grouped by why each module is here, because the only way to keep it honest is
    /// to be able to read it. Adding a feature means adding its modules here, in the same change.
    /// Asterisk works out load order from the dependencies the modules declare, so this file does
    /// not try to.
    /// </summary>
    public static class ModulesConfRenderer
    {
        private static readonly (string Purpose, string[] Modules)[] Allowlist =
        {
            ("Core services everything else needs", new[]
            {
                "res_pjproject.so",         // the PJPROJECT bridge res_pjsip is built on
                "res_rtp_asterisk.so",      // the RTP stack: no media without it
                "res_timing_timerfd.so",    // a timing source, or playback and recording drift
                "res_security_log.so",      // failed logins etc. to the security log (D7)
            }),

            ("Sorcery backends: how PJSIP objects and registrations are stored", new[]
            {
                "res_sorcery_config.so",    // objects that come from pjsip.conf
                "res_sorcery_memory.so",    // objects created at runtime
                "res_sorcery_astdb.so",     // contacts that survive a restart
            }),

            ("PJSIP: the only channel driver we use", new[]
            {
                "res_pjsip.so",
                "res_pjsip_session.so",
                "res_pjsip_pubsub.so",
                "res_pjsip_header_funcs.so",                      // PJSIP_HEADER(): read the To header for inbound DIDs
                "func_cut.so",                                     // CUT(): extract the DID out of the To header
                "chan_pjsip.so",
                "res_pjsip_authenticator_digest.so",          // check the password a phone sends
                "res_pjsip_outbound_authenticator_digest.so", // answer a provider's challenge
                "res_pjsip_endpoint_identifier_user.so",      // match a request to an endpoint by user
                "res_pjsip_endpoint_identifier_ip.so",        // match an inbound trunk call by address
                "res_pjsip_registrar.so",                     // accept REGISTER from phones
                "res_pjsip_outbound_registration.so",         // REGISTER with a provider ourselves
                "res_pjsip_sdp_rtp.so",                  // negotiate the media stream
                "res_pjsip_caller_id.so",                // the callerid we set per endpoint
                "res_pjsip_nat.so",                      // rewrite_contact, force_rport, 1:1 NAT
                "res_pjsip_dtmf_info.so",                // DTMF from phones that send SIP INFO
                "res_pjsip_notify.so",                   // loads pjsip_notify.conf, the Yealink NOTIFY push (D91)
                "res_pjsip_mwi.so",                      // answer a phone's SUBSCRIBE: the message-waiting light (D108)
                "res_pjsip_mwi_body_generator.so",       // writes the message-summary body those NOTIFYs carry (D108)
                "res_pjsip_exten_state.so",              // answer a phone's SUBSCRIBE for a hint: the BLF lamp (D121)
                "res_pjsip_dialog_info_body_generator.so", // the dialog-info+xml body a BLF key asks for (D121)
            }),

            ("Bridging two people together", new[]
            {
                "bridge_simple.so",
                "bridge_native_rtp.so",
                "bridge_holding.so",        // where a parked call waits: res_parking needs it (D119)
            }),

            ("Call parking and the music a parked caller hears (D119)", new[]
            {
                "res_parking.so",           // res_parking.conf, the Park/ParkedCall applications
                "res_musiconhold.so",       // musiconhold.conf, the one generated hold class
            }),

            ("Dialplan and the applications it calls", new[]
            {
                "pbx_config.so",            // extensions.conf itself
                "app_dial.so",              // Dial()
                "app_directed_pickup.so",   // Pickup(): *8 takes a call ringing elsewhere (D120)
                "app_playback.so",          // Playback(): prompts, and the announcements (D55)
                "app_echo.so",              // Echo(), the *43 test
                "app_voicemail.so",         // VoiceMail() and VoiceMailMain()
                "func_callerid.so",         // ${CALLERID(num)}, which *97 needs
                "func_timeout.so",          // TIMEOUT(digit), the gap an IVR allows between keys (D61)
            }),

            ("Audio: the formats our prompts and messages are stored in", new[]
            {
                "codec_alaw.so",
                "codec_g722.so",             // wideband HD voice, offered to endpoints that ask (D117)
                "format_g722.so",            // reads the .g722 music on hold tracks (D122)
                "codec_ulaw.so",             // slin is core in Asterisk 22 - it has no module to load (D117)
                "codec_gsm.so",             // prompts ship as GSM, voicemail records wav49
                "format_gsm.so",
                "format_pcm.so",
                "format_wav.so",            // reads the 8 kHz mono WAV an announcement is stored as (D55)
                "format_wav_gsm.so",
            }),
        };

        /// <summary>Every module we load, in the order the file lists them.</summary>
        public static IReadOnlyList<string> Modules =>
            Allowlist.SelectMany(group => group.Modules).ToList();

        public static string Render()
        {
            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append("[modules]\n");
            sb.Append("autoload = no\n");

            foreach (var (purpose, modules) in Allowlist)
            {
                sb.Append('\n');
                sb.Append($"; {ConfText.Safe(purpose, "module group")}\n");

                foreach (var module in modules)
                    sb.Append($"load = {ConfText.Safe(module, "module")}\n");
            }

            return sb.ToString();
        }
    }
}
