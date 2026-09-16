using System.Text;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders extensions.conf (the dialplan). Each extension gets an explicit entry rather
    /// than a pattern, so only numbers that exist in the database can be dialled.
    /// </summary>
    public static class ExtensionsConfRenderer
    {
        public const string InternalContext = "internal";
        public const string EchoTestNumber = "*43";

        /// <summary>
        /// Where a number that matched no outbound route ends up: its own context, included last,
        /// so that every route has already been tried (D46).
        /// </summary>
        public const string BlockedContext = "outbound-blocked";

        /// <summary>
        /// The context the internal one includes to reach the outside. It holds nothing but
        /// includes, in the order the routes are tried (D46).
        /// </summary>
        public const string OutboundContext = "outbound";

        /// <summary>How long an outbound call rings before giving up, in seconds.</summary>
        private const int OutboundRingSeconds = 60;

        /// <summary>FreePBX's number for "listen to my own messages", which users already know.</summary>
        public const string VoicemailMainNumber = "*97";

        public static string Render(IEnumerable<Extension> extensions) =>
            Render(extensions, new List<Trunk>(), new List<OutboundRoute>());

        public static string Render(IEnumerable<Extension> extensions, IEnumerable<Trunk> trunks) =>
            Render(extensions, trunks, new List<OutboundRoute>());

        public static string Render(IEnumerable<Extension> extensions, IEnumerable<Trunk> trunks, IEnumerable<OutboundRoute> routes)
        {
            var enabled = ConfText.EnabledInOrder(extensions);
            var trunkList = PjsipConfRenderer.TrunkRenderOrder(trunks);
            var routeList = RouteRenderOrder(routes, trunkList);

            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append($"[{InternalContext}]\n");
            sb.Append("; Echo test\n");
            sb.Append($"exten => {EchoTestNumber},1,Answer()\n");
            sb.Append(" same => n,Playback(demo-echotest)\n");
            sb.Append(" same => n,Echo()\n");
            sb.Append(" same => n,Hangup()\n");

            // Nobody has a mailbox, so the feature code would only ever say "no such mailbox".
            if (enabled.Any(e => e.VoicemailEnabled))
            {
                sb.Append('\n');
                sb.Append("; Check your own voicemail\n");
                sb.Append($"exten => {VoicemailMainNumber},1,Answer()\n");
                sb.Append($" same => n,VoiceMailMain(${{CALLERID(num)}}@{VoicemailConfRenderer.MailboxContext},s)\n");
                sb.Append(" same => n,Hangup()\n");
            }

            foreach (var extension in enabled)
            {
                var number = ConfText.Safe(extension.Number, "number");
                var name = ConfText.Safe(extension.Name, "name");

                sb.Append('\n');
                sb.Append($"; {name}\n");
                sb.Append($"exten => {number},1,Dial(PJSIP/{number},30)\n");

                if (extension.VoicemailEnabled)
                {
                    // Busy gets the "busy" greeting, everything else (no answer, phone off,
                    // congestion) gets "unavailable" (D29). Which greeting is this renderer's
                    // decision; what "send it to the mailbox" looks like is not (D36).
                    var mailbox = new Destination(DestinationType.Voicemail, extension.Number);

                    sb.Append(" same => n,GotoIf($[\"${DIALSTATUS}\" = \"BUSY\"]?busy:unavailable)\n");
                    sb.Append(DestinationDialplan.Lines(mailbox, "busy", VoicemailGreeting.Busy));
                    sb.Append(DestinationDialplan.Lines(mailbox, "unavailable", VoicemailGreeting.Unavailable));
                }
                else
                {
                    sb.Append(DestinationDialplan.Lines(Destination.Hangup));
                }
            }

            // Extensions in a context are matched before anything it includes, so the phones and
            // feature codes above always win over a route pattern (D46).
            if (routeList.Count > 0)
            {
                sb.Append('\n');
                sb.Append("; Calls to the outside world, tried in the order the routes are listed\n");
                sb.Append($"include => {OutboundContext}\n");
            }

            foreach (var trunk in trunkList)
            {
                var trunkName = ConfText.Safe(trunk.Name, "trunk name");

                // Where inbound calls from this provider land. Until inbound routes exist
                // (piece 11) there is nowhere to send them, so they end here rather than
                // anywhere surprising (D38).
                sb.Append('\n');
                sb.Append($"[{ConfText.Safe(trunk.Context, "trunk context")}]\n");
                sb.Append($"; Inbound calls from the {trunkName} trunk. No inbound routes yet.\n");
                sb.Append($"exten => _X.,1,NoOp(Inbound call on trunk {trunkName} for ${{EXTEN}})\n");
                sb.Append(DestinationDialplan.Lines(Destination.Hangup));
            }

            AppendOutboundRoutes(sb, routeList, trunkList);

            return sb.ToString();
        }

        /// <summary>
        /// Enabled routes in the order they are tried, leaving out any whose trunk is not there to
        /// take the call. Re-validated, so a row that reached the database another way cannot
        /// reach a conf file.
        /// </summary>
        public static List<OutboundRoute> RouteRenderOrder(IEnumerable<OutboundRoute> routes, List<Trunk> enabledTrunks)
        {
            var enabled = routes.Where(r => r.Enabled).ToList();

            foreach (var route in enabled)
            {
                var errors = route.Validate();
                if (errors.Count > 0)
                    throw new InvalidOperationException($"Outbound route '{route.Name}' is invalid: {string.Join(" ", errors)}");
            }

            return enabled
                .Where(r => enabledTrunks.Any(t => t.TrunkID == r.TrunkID))
                .OrderBy(r => r.Priority)
                .ThenBy(r => r.Name, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// One context per route, and one context that includes them in order, because Asterisk
        /// searches includes in the order they are written but picks its own idea of the best
        /// match within a single context (D46). The blocked context is included last, so a number
        /// only reaches it when every route has been tried.
        /// </summary>
        private static void AppendOutboundRoutes(StringBuilder sb, List<OutboundRoute> routes, List<Trunk> trunks)
        {
            if (routes.Count == 0)
                return;

            sb.Append('\n');
            sb.Append($"[{OutboundContext}]\n");
            foreach (var route in routes)
                sb.Append($"include => {ConfText.Safe(route.Context, "route context")}\n");

            // Last, so that a number nothing matched fails here rather than anywhere else (D45).
            sb.Append($"include => {BlockedContext}\n");

            foreach (var route in routes)
            {
                var trunk = trunks.Single(t => t.TrunkID == route.TrunkID);
                var name = ConfText.Safe(route.Name, "route name");
                var pattern = ConfText.Safe(route.DialPattern, "dial pattern");
                var trunkName = ConfText.Safe(trunk.Name, "trunk name");
                var trunkHost = ConfText.Safe(trunk.ServerHost, "trunk server host");

                sb.Append('\n');
                sb.Append($"[{ConfText.Safe(route.Context, "route context")}]\n");
                sb.Append($"; {name} ({route.Priority}) out over {trunkName}\n");
                // The full URI form is required: chan_pjsip treats a bare dialstring as a literal
                // URI and rejects it ("Could not create dialog to invalid URI").
                sb.Append($"exten => {pattern},1,Dial(PJSIP/{trunkName}/sip:${{EXTEN}}@{trunkHost},{OutboundRingSeconds})\n");
                sb.Append(" same => n,Hangup()\n");
            }

            sb.Append('\n');
            sb.Append($"[{BlockedContext}]\n");
            sb.Append("; No route matched, so the call does not go out. Toll fraud starts with a\n");
            sb.Append("; number nobody meant to allow, so unmatched numbers fail here on purpose.\n");
            sb.Append("exten => _X.,1,NoOp(No outbound route for ${EXTEN})\n");
            sb.Append(" same => n,Playback(ss-noservice)\n");
            sb.Append(" same => n,Hangup()\n");
        }
    }
}
