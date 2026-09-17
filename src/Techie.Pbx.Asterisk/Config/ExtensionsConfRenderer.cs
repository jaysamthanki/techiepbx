using System.Text;
using Techie.Pbx.Asterisk.Audio;
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
            IEnumerable<InboundRoute> inbound) =>
            Render(extensions, trunks, routes, inbound, new List<RingGroup>());

        public static string Render(
            IEnumerable<Extension> extensions,
            IEnumerable<Trunk> trunks,
            IEnumerable<OutboundRoute> routes,
            IEnumerable<InboundRoute> inbound,
            IEnumerable<RingGroup> ringGroups) =>
            Render(extensions, trunks, routes, inbound, ringGroups, new List<Announcement>());

        public static string Render(
            IEnumerable<Extension> extensions,
            IEnumerable<Trunk> trunks,
            IEnumerable<OutboundRoute> routes,
            IEnumerable<InboundRoute> inbound,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements)
        {
            var enabled = ConfText.EnabledInOrder(extensions);
            var trunkList = PjsipConfRenderer.TrunkRenderOrder(trunks);
            var routeList = RouteRenderOrder(routes, trunkList);
            var inboundList = InboundRenderOrder(inbound, trunkList);
            var groupList = RingGroupRenderOrder(ringGroups, enabled);
            var announcementList = AnnouncementRenderOrder(announcements);

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

            foreach (var group in groupList)
                AppendRingGroup(sb, group, enabled);

            foreach (var announcement in announcementList)
                AppendAnnouncement(sb, announcement);

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
        /// The announcements that get a dialplan entry, by play extension. Re-validated, so a row
        /// that reached the database another way cannot reach a conf file.
        ///
        /// Three things are needed before an announcement is worth writing: it is switched on, it
        /// has audio, and it has a number to dial. Without the number there is no extension to
        /// write; without the audio the entry would be a Playback of nothing, which Asterisk
        /// reports mid-call as a warning rather than refusing up front (D56).
        /// </summary>
        public static List<Announcement> AnnouncementRenderOrder(IEnumerable<Announcement> announcements)
        {
            var enabled = announcements.Where(a => a.Enabled).ToList();

            foreach (var announcement in enabled)
            {
                var errors = announcement.Validate();
                if (errors.Count > 0)
                    throw new InvalidOperationException($"Announcement '{announcement.Name}' is invalid: {string.Join(" ", errors)}");
            }

            return enabled
                .Where(a => a.IsPlayable)
                .OrderBy(a => a.PlayExtension.Length)
                .ThenBy(a => a.PlayExtension, StringComparer.Ordinal)
                .ToList();
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
        /// Enabled ring groups by number, leaving out any with nobody left to ring. Re-validated,
        /// so a row that reached the database another way cannot reach a conf file.
        /// </summary>
        public static List<RingGroup> RingGroupRenderOrder(IEnumerable<RingGroup> ringGroups, List<Extension> enabledExtensions)
        {
            var enabled = ringGroups.Where(g => g.Enabled).ToList();

            foreach (var group in enabled)
            {
                var errors = group.Validate();
                if (errors.Count > 0)
                    throw new InvalidOperationException($"Ring group '{group.Number}' is invalid: {string.Join(" ", errors)}");
            }

            // A group whose every member has been deleted or switched off would be a Dial() with
            // nothing to dial, which Asterisk treats as an error mid-call. Better to leave it out
            // and let the number simply not exist.
            return enabled
                .Where(g => MembersOf(g, enabledExtensions).Count > 0)
                .OrderBy(g => g.Number.Length)
                .ThenBy(g => g.Number, StringComparer.Ordinal)
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
        /// One announcement: answer, play the file, hang up (D56). The entry lives in the internal
        /// context like an extension, so a user can dial it and a destination can Goto it, and
        /// both get the same three lines.
        ///
        /// Answer() is written unconditionally. A call arriving here from a phone or from a trunk
        /// has not been answered yet and Playback on an unanswered channel is early media at best;
        /// on a channel that is already up — a Goto from somewhere that answered first — Asterisk's
        /// Answer application returns immediately and does nothing, so there is nothing to guard
        /// and no ${CHANNEL(state)} test worth the modules it would need.
        /// </summary>
        private static void AppendAnnouncement(StringBuilder sb, Announcement announcement)
        {
            var number = ConfText.Safe(announcement.PlayExtension, "announcement play extension");
            var name = ConfText.Safe(announcement.Name, "announcement name");

            // The file name was derived by us and matched against a strict pattern before it was
            // stored, and it still goes through Safe: a row that arrived another way cannot write
            // a second Playback argument or comment the rest of the entry out.
            var prompt = ConfText.Safe(AnnouncementStore.PlaybackName(announcement), "announcement prompt");

            sb.Append('\n');
            sb.Append($"; {name}\n");

            if (announcement.Description.Length > 0)
                sb.Append($"; {ConfText.Safe(announcement.Description, "announcement description")}\n");

            sb.Append($"exten => {number},1,Answer()\n");
            sb.Append($" same => n,Playback({prompt})\n");
            sb.Append(DestinationDialplan.Lines(Destination.Hangup));
        }

        /// <summary>
        /// One ring group: an entry in the internal context that rings its members and then sends
        /// the call wherever the group says (D52). Generated Dial() rather than a queue, so there
        /// is nothing to load and nothing to keep in step.
        ///
        /// Ring all is one Dial with the members joined by &amp;. Hunt is one Dial each, in order:
        /// Dial only carries on to the next priority when nobody answered, so an answered call
        /// ends where it was answered rather than ringing the next member afterwards.
        /// </summary>
        private static void AppendRingGroup(StringBuilder sb, RingGroup group, List<Extension> enabledExtensions)
        {
            var number = ConfText.Safe(group.Number, "ring group number");
            var name = ConfText.Safe(group.Name, "ring group name");
            var members = MembersOf(group, enabledExtensions)
                .Select(m => $"PJSIP/{ConfText.Safe(m, "ring group member")}")
                .ToList();

            sb.Append('\n');
            sb.Append($"; {name} ({group.ToStrategy()}, {group.RingSeconds}s)\n");
            sb.Append($"exten => {number},1,NoOp(Ring group {number} {name})\n");

            if (group.CallerIDPrefix.Length > 0)
            {
                // SafeField, not Safe: a comma in the prefix would end the Set() argument early
                // and quietly drop the rest.
                var prefix = ConfText.SafeField(group.CallerIDPrefix, "caller ID prefix");
                sb.Append($" same => n,Set(CALLERID(name)={prefix}${{CALLERID(name)}})\n");
            }

            if (group.ToStrategy() == RingStrategy.All)
            {
                sb.Append($" same => n,Dial({string.Join("&", members)},{group.RingSeconds})\n");
            }
            else
            {
                foreach (var member in members)
                    sb.Append($" same => n,Dial({member},{group.RingSeconds})\n");
            }

            sb.Append(DestinationDialplan.Lines(group.ToDestination()));
        }

        /// <summary>
        /// The group's members that are still extensions somebody can ring, in the order the group
        /// lists them. A member that has been deleted or switched off is dropped rather than
        /// written into a Dial that would fail.
        /// </summary>
        private static List<string> MembersOf(RingGroup group, List<Extension> enabledExtensions) =>
            group.MemberList()
                .Where(m => enabledExtensions.Any(e => string.Equals(e.Number, m, StringComparison.Ordinal)))
                .ToList();

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
            sb.Append("; The provider puts the account user in the request URI and the DID in the To\n");
            sb.Append("; header (verified on the wire, matching Callcentric's DID-routing guide), so\n");
            sb.Append("; every call lands on one pattern and is dispatched by the number it was sent to.\n");

            // The dispatch block: DID routes first, then the fallthrough (D50). A GotoIf with
            // only a true target falls through to the next line, so the checks chain naturally.
            sb.Append("exten => _X.,1,Set(DID=${CUT(CUT(PJSIP_HEADER(read,To),@,1),:,2)})\n");
            sb.Append($" same => n,NoOp(Inbound ${{DID}} on {trunkName})\n");

            foreach (var route in routes.Where(r => !r.CatchAll))
            {
                // Matched on the DID in the To header, character for character (D51).
                var number = ConfText.Safe(route.DialplanExtension, "DID");
                var label = $"r{route.InboundRouteID}";

                sb.Append($" same => n,GotoIf($[\"${{DID}}\" = \"{number}\"]?{label})\n");
            }

            // Nothing claimed the call, so it ends here: unanswered, so the caller's own carrier
            // tells them, and above all never falling through to somewhere that could dial out
            // (D50). A catch-all route claims it instead.
            var catchAll = routes.FirstOrDefault(r => r.CatchAll);
            if (catchAll is null)
            {
                sb.Append($" same => n(none),NoOp(No inbound route for ${{DID}} on {trunkName})\n");
                sb.Append(DestinationDialplan.Lines(Destination.Hangup));
            }
            else
            {
                var destination = catchAll.ToDestination();
                sb.Append($" same => n(none),NoOp(Inbound catch-all on {trunkName} to {ConfText.Safe(destination.Key, "destination")})\n");
                sb.Append(DestinationDialplan.Lines(destination));
            }

            // The route targets: each is a labeled jump destination for its GotoIf above.
            foreach (var route in routes.Where(r => !r.CatchAll))
            {
                var number = ConfText.Safe(route.DialplanExtension, "DID");
                var destination = route.ToDestination();
                var label = $"r{route.InboundRouteID}";

                sb.Append($" same => n({label}),NoOp(Inbound {number} on {trunkName} to {ConfText.Safe(destination.Key, "destination")})\n");
                sb.Append(DestinationDialplan.Lines(destination));
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
