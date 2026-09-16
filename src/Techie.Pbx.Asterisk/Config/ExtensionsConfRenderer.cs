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
            Render(extensions, new List<Trunk>(), new List<OutboundRoute>(), new List<InboundRoute>());

        public static string Render(IEnumerable<Extension> extensions, IEnumerable<Trunk> trunks) =>
            Render(extensions, trunks, new List<OutboundRoute>(), new List<InboundRoute>());

        public static string Render(IEnumerable<Extension> extensions, IEnumerable<Trunk> trunks, IEnumerable<OutboundRoute> routes) =>
            Render(extensions, trunks, routes, new List<InboundRoute>());

        public static string Render(
            IEnumerable<Extension> extensions,
            IEnumerable<Trunk> trunks,
            IEnumerable<OutboundRoute> routes,
            IEnumerable<InboundRoute> inbound)
        {
            var enabled = ConfText.EnabledInOrder(extensions);
            var trunkList = PjsipConfRenderer.TrunkRenderOrder(trunks);
            var routeList = RouteRenderOrder(routes, trunkList);
            var inboundList = InboundRenderOrder(inbound, trunkList);

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
                AppendTrunkContext(sb, trunk, inboundList.Where(r => r.TrunkID == trunk.TrunkID).ToList());

            AppendOutboundRoutes(sb, routeList, trunkList);

            return sb.ToString();
        }

        /// <summary>
        /// Enabled inbound routes for trunks that exist, DIDs first and each trunk's catch-all
        /// last. Re-validated, so a row that reached the database another way cannot reach a conf
        /// file.
        /// </summary>
        public static List<InboundRoute> InboundRenderOrder(IEnumerable<InboundRoute> routes, List<Trunk> enabledTrunks)
        {
            var enabled = routes.Where(r => r.Enabled).ToList();

            foreach (var route in enabled)
            {
                var errors = route.Validate();
                if (errors.Count > 0)
                {
                    var what = route.CatchAll ? "catch-all" : route.DID;
                    throw new InvalidOperationException($"Inbound route '{what}' is invalid: {string.Join(" ", errors)}");
                }
            }

            return enabled
                .Where(r => enabledTrunks.Any(t => t.TrunkID == r.TrunkID))
                .OrderBy(r => r.CatchAll)
                .ThenBy(r => r.DID, StringComparer.Ordinal)
                .ToList();
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
        /// Where calls from one provider land: one entry per DID, sent on by the shared
        /// destination helper (D36), and one entry for everything else — the trunk's catch-all
        /// route if it has one, otherwise a hangup (D50).
        ///
        /// No includes and no ordering tricks are needed here, unlike outbound (D46): the DIDs are
        /// literal extensions and the catch-all is a pattern, and Asterisk always prefers a literal
        /// match to a pattern.
        /// </summary>
        private static void AppendTrunkContext(StringBuilder sb, Trunk trunk, List<InboundRoute> routes)
        {
            var trunkName = ConfText.Safe(trunk.Name, "trunk name");

            sb.Append('\n');
            sb.Append($"[{ConfText.Safe(trunk.Context, "trunk context")}]\n");
            sb.Append($"; Inbound calls from the {trunkName} trunk\n");

            foreach (var route in routes)
            {
                // Matched as the provider sends it, character for character (D51).
                var number = ConfText.Safe(route.DialplanExtension, "DID");
                var destination = route.ToDestination();
                var what = route.CatchAll ? "any other number" : number;

                if (route.Description.Length > 0)
                    sb.Append($"; {ConfText.Safe(route.Description, "description")}\n");

                sb.Append($"exten => {number},1,NoOp(Inbound {what} on {trunkName} to {ConfText.Safe(destination.Key, "destination")})\n");
                sb.Append(DestinationDialplan.Lines(destination));
            }

            // Nothing claimed the rest, so the call ends here: unanswered, so the caller's own
            // carrier tells them, and above all never falling through to somewhere that could
            // dial out (D50).
            if (!routes.Any(r => r.CatchAll))
            {
                sb.Append($"exten => _X.,1,NoOp(No inbound route for ${{EXTEN}} on {trunkName})\n");
                sb.Append(DestinationDialplan.Lines(Destination.Hangup));
            }
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
