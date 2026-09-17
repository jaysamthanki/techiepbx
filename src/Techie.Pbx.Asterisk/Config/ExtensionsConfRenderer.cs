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

        /// <summary>
        /// Where an IVR sends a caller who has run out of retries: a named extension in the menu's
        /// own context, so the final destination is written once however many ways lead to it.
        /// A caller cannot reach it by pressing keys — a keypad cannot spell it.
        /// </summary>
        public const string IvrFinalExtension = "final";

        /// <summary>
        /// Asterisk's own "the caller pressed something this context has no extension for"
        /// extension. Every digit an IVR's menu does not use lands here (D59).
        /// </summary>
        public const string IvrInvalidExtension = "i";

        /// <summary>
        /// "I'm sorry, that's not a valid extension", from Asterisk's core sounds — the same file
        /// its own sample dialplan plays from an `i` extension.
        /// </summary>
        public const string IvrInvalidPrompt = "invalid";

        /// <summary>How many times round the menu this caller has been, so far.</summary>
        public const string IvrRetriesVariable = "IVR_RETRIES";

        /// <summary>The priority label the retry paths jump back to: the greeting, not the setup.</summary>
        public const string IvrStartLabel = "start";

        /// <summary>
        /// Asterisk's own "the caller pressed nothing in time" extension, which is where WaitExten
        /// sends the call by itself.
        /// </summary>
        public const string IvrTimeoutExtension = "t";

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
            IEnumerable<Announcement> announcements) =>
            Render(extensions, trunks, routes, inbound, ringGroups, announcements, new List<Ivr>());

        public static string Render(
            IEnumerable<Extension> extensions,
            IEnumerable<Trunk> trunks,
            IEnumerable<OutboundRoute> routes,
            IEnumerable<InboundRoute> inbound,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements,
            IEnumerable<Ivr> ivrs)
        {
            var enabled = ConfText.EnabledInOrder(extensions);
            var trunkList = PjsipConfRenderer.TrunkRenderOrder(trunks);
            var routeList = RouteRenderOrder(routes, trunkList);
            var inboundList = InboundRenderOrder(inbound, trunkList);
            var groupList = RingGroupRenderOrder(ringGroups, enabled);

            // The raw rows as well as the render order: an announcement that is only ever an IVR's
            // greeting has no play extension, so it is not in the list that gets dialplan entries.
            var announcementRows = announcements.ToList();
            var announcementList = AnnouncementRenderOrder(announcementRows);
            var ivrList = IvrRenderOrder(ivrs, announcementRows);

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

            foreach (var ivr in ivrList)
                AppendIvrEntry(sb, ivr);

            // Extensions in a context are matched before anything it includes, so the phones and
            // feature codes above always win over a route pattern (D46).
            if (routeList.Count > 0)
            {
                sb.Append('\n');
                sb.Append("; Calls to the outside world, tried in the order the routes are listed\n");
                sb.Append($"include => {OutboundContext}\n");
            }

            foreach (var ivr in ivrList)
                AppendIvrContext(sb, ivr, ivr.GreetingIn(announcementRows)!, enabled);

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
        /// The IVRs that get a menu, by play extension. Re-validated, so a row that reached the
        /// database another way cannot reach a conf file.
        ///
        /// Three things are needed, the same three an announcement needs (D56) with the greeting
        /// standing in for the audio: the IVR is switched on, it has a number to dial, and the
        /// announcement it greets with is there, switched on and has audio. Without the greeting
        /// the menu would answer and then sit in silence, which is worse than the number simply
        /// not existing (D58).
        /// </summary>
        public static List<Ivr> IvrRenderOrder(IEnumerable<Ivr> ivrs, IEnumerable<Announcement> announcements)
        {
            var enabled = ivrs.Where(i => i.Enabled).ToList();
            var announcementList = announcements.ToList();

            foreach (var ivr in enabled)
            {
                var errors = ivr.Validate();
                if (errors.Count > 0)
                    throw new InvalidOperationException($"IVR '{ivr.Name}' is invalid: {string.Join(" ", errors)}");
            }

            return enabled
                .Where(i => i.IsPlayable && i.GreetingIn(announcementList) != null)
                .OrderBy(i => i.PlayExtension.Length)
                .ThenBy(i => i.PlayExtension, StringComparer.Ordinal)
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
        /// One IVR's menu, in a context of its own (D59).
        ///
        /// A context per IVR rather than entries in <see cref="InternalContext"/>, because the keys
        /// a caller presses are matched as extensions of the context the call is in: "press 1"
        /// means an extension called <c>1</c>, and putting those in the internal context would make
        /// single digits dialable from every phone in the building. The menu is reached only by the
        /// Goto on its play extension, and — like a trunk's context (D50) — it includes nothing, so
        /// a caller from outside can never fall through to anywhere that dials out.
        /// </summary>
        private static void AppendIvrContext(StringBuilder sb, Ivr ivr, Announcement greeting, List<Extension> enabledExtensions)
        {
            var context = ConfText.Safe(ivr.Context, "IVR context");
            var number = ConfText.Safe(ivr.PlayExtension, "IVR play extension");
            var name = ConfText.Safe(ivr.Name, "IVR name");

            // The greeting is the announcement's own stored file, played from where the
            // announcement feature already put it (D58). Safe() again, as everywhere else.
            var prompt = ConfText.Safe(AnnouncementStore.PlaybackName(greeting), "IVR greeting prompt");
            var final = ivr.ToFinalDestination();

            sb.Append('\n');
            sb.Append($"[{context}]\n");
            sb.Append($"; {name}: the menu itself, reached by the Goto on {number} in [{InternalContext}].\n");
            sb.Append("; Nothing is included here, so a caller can never fall through to a route out.\n");
            sb.Append("exten => s,1,Answer()\n");

            // The gap allowed between digits of a directly dialled extension. WaitExten's own
            // argument is the wait for the first one.
            sb.Append($" same => n,Set(TIMEOUT(digit)={ivr.TimeoutSeconds})\n");
            sb.Append($" same => n,Set({IvrRetriesVariable}=0)\n");

            // Background rather than Playback: a caller who already knows the menu can press a key
            // over the greeting instead of sitting through it.
            sb.Append($" same => n({IvrStartLabel}),Background({prompt})\n");
            sb.Append($" same => n,WaitExten({ivr.TimeoutSeconds})\n");

            // WaitExten sends a caller who pressed nothing to 't' itself, and a caller who pressed
            // something unusable goes to 'i'. This line is for neither happening.
            sb.Append($" same => n,Goto({IvrTimeoutExtension},1)\n");

            foreach (var entry in ivr.Entries.OrderBy(e => IvrEntry.Rank(e.Digit)))
            {
                var digit = ConfText.Safe(entry.Digit, "IVR key");
                var destination = entry.ToDestination();

                sb.Append('\n');
                sb.Append($"exten => {digit},1,NoOp(IVR {number} key {digit} to {ConfText.Safe(destination.Key, "destination")})\n");
                sb.Append(DestinationDialplan.Lines(destination));
            }

            if (ivr.EnableDirectDial && enabledExtensions.Count > 0)
            {
                sb.Append('\n');
                sb.Append("; Dial an extension straight from the menu. One entry each rather than a\n");
                sb.Append("; pattern, as in the internal context, so only numbers that exist can be\n");
                sb.Append("; reached from here (D12, D60).\n");

                foreach (var extension in enabledExtensions)
                {
                    var extensionNumber = ConfText.Safe(extension.Number, "number");
                    sb.Append($"exten => {extensionNumber},1,Goto({InternalContext},{extensionNumber},1)\n");
                }
            }

            sb.Append('\n');
            sb.Append("; Nothing was pressed in time\n");
            sb.Append($"exten => {IvrTimeoutExtension},1,NoOp(IVR {number} timed out)\n");
            AppendIvrRetry(sb, ivr);
            sb.Append($" same => n,Goto(s,{IvrStartLabel})\n");

            sb.Append('\n');
            sb.Append("; A key with nothing behind it: every digit the menu does not use lands here\n");
            sb.Append($"exten => {IvrInvalidExtension},1,NoOp(IVR {number} invalid entry ${{EXTEN}})\n");
            AppendIvrRetry(sb, ivr);
            sb.Append($" same => n,Playback({IvrInvalidPrompt})\n");
            sb.Append($" same => n,Goto(s,{IvrStartLabel})\n");

            sb.Append('\n');
            sb.Append("; Out of retries, so the call goes where the menu says it should\n");
            sb.Append($"exten => {IvrFinalExtension},1,NoOp(IVR {number} giving up to {ConfText.Safe(final.Key, "destination")})\n");
            sb.Append(DestinationDialplan.Lines(final));
        }

        /// <summary>
        /// The way into an IVR: one entry in the internal context, exactly as an announcement gets
        /// one (D57). A user dialling the number, an inbound route and a destination all arrive
        /// through it, so there is one description of how a menu starts rather than three.
        /// </summary>
        private static void AppendIvrEntry(StringBuilder sb, Ivr ivr)
        {
            var number = ConfText.Safe(ivr.PlayExtension, "IVR play extension");
            var name = ConfText.Safe(ivr.Name, "IVR name");

            sb.Append('\n');
            sb.Append($"; {name} (menu)\n");

            if (ivr.Description.Length > 0)
                sb.Append($"; {ConfText.Safe(ivr.Description, "IVR description")}\n");

            sb.Append($"exten => {number},1,Goto({ConfText.Safe(ivr.Context, "IVR context")},s,1)\n");
        }

        /// <summary>
        /// The two lines both the timeout and the invalid paths start with: count this attempt,
        /// and give up once there have been more of them than the IVR allows. Retries is how many
        /// second chances a caller gets, so 0 means the first mistake ends the menu.
        /// </summary>
        private static void AppendIvrRetry(StringBuilder sb, Ivr ivr)
        {
            sb.Append($" same => n,Set({IvrRetriesVariable}=$[${{{IvrRetriesVariable}}} + 1])\n");
            sb.Append($" same => n,GotoIf($[${{{IvrRetriesVariable}}} > {ivr.Retries}]?{IvrFinalExtension},1)\n");
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
