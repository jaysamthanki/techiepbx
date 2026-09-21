using System.Globalization;
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
        /// The options every generated Dial() carries, so that the features.conf featuremap is
        /// consulted at all during a call (D119). Asterisk's DTMF features are opt-in per Dial and
        /// per side: <c>t</c> and <c>T</c> let the called and the calling party transfer,
        /// <c>k</c> and <c>K</c> let them park. Both halves of each pair, because "whoever is on
        /// the call may park it" is what an admin means by call parking, and which end of the call
        /// dialled is not something they think about.
        ///
        /// The cost, and it is worth knowing: <c>t</c>/<c>T</c> also turn on blind transfer, whose
        /// Asterisk default is <c>#</c>. Pressing # mid-call now starts one.
        ///
        /// A Dial to a phone in this building adds one more on top of these — see
        /// <see cref="InternalDialOptions"/>.
        /// </summary>
        public const string DialOptions = "tTkK";

        /// <summary>
        /// The context <c>Dial</c>'s <c>U()</c> option names, which is the only way this system has
        /// of running anything on the channel a <c>Dial</c> creates (D122 amended). A called phone's
        /// channel never executes dialplan of its own, so nothing here had ever named a hold class
        /// on it; <c>U(sub-setmoh)</c> Gosubs into the context below on that channel as it answers,
        /// and the subroutine backfills the class exactly as the caller's side does.
        ///
        /// The name is the context, and <c>U()</c> always enters it at <c>s,1</c>. It is written
        /// only when there is a class that ships to name, and the option is only added to a Dial
        /// when the context is there to reach — an empty <c>U()</c> target is a failed Gosub and a
        /// dropped call, not a quiet no-op.
        /// </summary>
        public const string SetMohContext = "sub-setmoh";

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
        /// Directed call pickup: dial this followed by the extension that is ringing to take the
        /// call from your own phone ("I hear 100 ringing and I'm at 103, so I dial *8100"). The
        /// pattern demands at least one digit after the code, so bare *8 matches nothing — this
        /// system has no pickup groups, only the directed form, and a code that does nothing is
        /// worse than none. FreePBX's number, for the same reason as *97.
        /// </summary>
        public const string PickupCode = "*8";

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

        /// <summary>
        /// Where a time condition sends a call outside its open hours. It is also where the checks
        /// fall through to, so it is written first of the three and needs no Goto of its own (D63).
        /// </summary>
        public const string TimeConditionClosedLabel = "closed";

        /// <summary>Where a holiday with no override of its own leads.</summary>
        public const string TimeConditionHolidayLabel = "holiday";

        /// <summary>
        /// And the prefix for a holiday that does have one: "h1" is the first such date of the
        /// year. Numbered by position rather than by row ID, so a saved condition renders the same
        /// whether its rules have been rewritten since or not.
        /// </summary>
        public const string TimeConditionHolidayPrefix = "h";

        /// <summary>Where a time condition sends a call inside its open hours.</summary>
        public const string TimeConditionOpenLabel = "open";

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
            IEnumerable<Ivr> ivrs) =>
            Render(extensions, trunks, routes, inbound, ringGroups, announcements, ivrs, new List<TimeCondition>());

        public static string Render(
            IEnumerable<Extension> extensions,
            IEnumerable<Trunk> trunks,
            IEnumerable<OutboundRoute> routes,
            IEnumerable<InboundRoute> inbound,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements,
            IEnumerable<Ivr> ivrs,
            IEnumerable<TimeCondition> timeConditions) =>
            Render(extensions, trunks, routes, inbound, ringGroups, announcements, ivrs, timeConditions, AsteriskSettings.DefaultTimezone);

        public static string Render(
            IEnumerable<Extension> extensions,
            IEnumerable<Trunk> trunks,
            IEnumerable<OutboundRoute> routes,
            IEnumerable<InboundRoute> inbound,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements,
            IEnumerable<Ivr> ivrs,
            IEnumerable<TimeCondition> timeConditions,
            string timezone) =>
            Render(extensions, trunks, routes, inbound, ringGroups, announcements, ivrs, timeConditions, timezone, new ParkingSettings());

        public static string Render(
            IEnumerable<Extension> extensions,
            IEnumerable<Trunk> trunks,
            IEnumerable<OutboundRoute> routes,
            IEnumerable<InboundRoute> inbound,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements,
            IEnumerable<Ivr> ivrs,
            IEnumerable<TimeCondition> timeConditions,
            string timezone,
            ParkingSettings parking) =>
            Render(extensions, trunks, routes, inbound, ringGroups, announcements, ivrs, timeConditions, timezone, parking, new List<MohClass>());

        /// <param name="timezone">
        /// The IANA zone a time condition's open hours are written in. It is named as the fifth
        /// argument of every GotoIfTime the condition contexts generate, so Asterisk evaluates the
        /// rule in that zone — DST and all — whatever the server's own clock is set to (D74).
        /// </param>
        /// <param name="parking">
        /// Call parking, which adds one entry per slot to the internal context when it is switched
        /// on and nothing at all when it is not (D119).
        /// </param>
        /// <param name="mohClasses">
        /// The music on hold classes, so that an inbound route naming one can be written as the
        /// class's name rather than its ID (D122 amended). A route naming a class that is not in
        /// this list is refused rather than written, the way every other dangling reference is.
        /// The one flagged <see cref="MohClass.IsDefault"/> is also what an internal call falls
        /// back to, which is the other half of the same amendment.
        /// </param>
        public static string Render(
            IEnumerable<Extension> extensions,
            IEnumerable<Trunk> trunks,
            IEnumerable<OutboundRoute> routes,
            IEnumerable<InboundRoute> inbound,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements,
            IEnumerable<Ivr> ivrs,
            IEnumerable<TimeCondition> timeConditions,
            string timezone,
            ParkingSettings parking,
            IEnumerable<MohClass> mohClasses)
        {
            parking.ThrowIfInvalid();

            var enabled = ConfText.EnabledInOrder(extensions);
            var trunkList = PjsipConfRenderer.TrunkRenderOrder(trunks);
            var routeList = RouteRenderOrder(routes, trunkList);
            var inboundList = InboundRenderOrder(inbound, trunkList);
            var groupList = RingGroupRenderOrder(ringGroups, enabled);
            var mohClassList = mohClasses.ToList();

            // The raw rows as well as the render order: an announcement that is only ever an IVR's
            // greeting has no play extension, so it is not in the list that gets dialplan entries.
            var announcementRows = announcements.ToList();
            var announcementList = AnnouncementRenderOrder(announcementRows);
            var ivrList = IvrRenderOrder(ivrs, announcementRows);
            var timeConditionList = TimeConditionRenderOrder(timeConditions);

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

            AppendParkingSlots(sb, parking);

            sb.Append('\n');
            sb.Append("; Directed call pickup: the code above followed by the extension that is ringing\n");
            sb.Append($"exten => _{PickupCode}.,1,Pickup(${{EXTEN:{PickupCode.Length}}}@{InternalContext})\n");
            sb.Append(" same => n,Hangup()\n");

            // What a caller whose call started on a phone here hears while they are held, written
            // ahead of every Dial below (D122 amended). Empty when there is no class that ships,
            // and then nothing at all is written.
            var internalMusic = InternalMusicOnHoldLine(DefaultOf(mohClassList));

            // The same backfill for the other end of the call, which needs the Gosub because a
            // Dial-created channel runs no dialplan of its own. Both halves stand or fall together:
            // no class that ships, no [sub-setmoh] context, and so no U() naming one.
            var internalDialOptions = InternalDialOptions(internalMusic.Length > 0);

            if (internalMusic.Length > 0 && enabled.Count > 0)
            {
                sb.Append('\n');
                sb.Append("; Hold music for a call that started on a phone here. Each extension below names\n");
                sb.Append("; the class that ships on the caller's channel before it dials, and only when\n");
                sb.Append("; Asterisk still has its own default sitting there: a class an inbound route\n");
                sb.Append("; already chose for this caller is theirs, and is left alone (D122 amended).\n");
                sb.Append($"; The U({SetMohContext}) on each Dial does the same for the channel it creates,\n");
                sb.Append("; which is the one that is held when the caller is the one pressing hold.\n");
            }

            foreach (var extension in enabled)
            {
                var number = ConfText.Safe(extension.Number, "number");
                var name = ConfText.Safe(extension.Name, "name");

                sb.Append('\n');
                sb.Append($"; {name}\n");

                // The hint is what a BLF key on another phone subscribes to: it is how a lamp
                // knows this extension is ringing or busy (D121). One per extension, whether or
                // not any phone is watching it — a hint costs a dialplan line and nothing else,
                // and a key assigned later must not need an apply to light up.
                sb.Append($"exten => {number},hint,PJSIP/{number}\n");

                // The hold music takes priority 1 when there is a class to name, which makes the
                // Dial priority 2. Everything that arrives here still arrives at priority 1: a
                // Goto from a route, a menu or another extension lands on the ExecIf and falls
                // through to the Dial, which is why it is a priority of its own and not a label.
                if (internalMusic.Length > 0)
                {
                    sb.Append($"exten => {number},1,{internalMusic}\n");
                    sb.Append($" same => n,Dial(PJSIP/{number},30,{internalDialOptions})\n");
                }
                else
                {
                    sb.Append($"exten => {number},1,Dial(PJSIP/{number},30,{internalDialOptions})\n");
                }

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
                AppendRingGroup(sb, group, enabled, internalDialOptions);

            foreach (var announcement in announcementList)
                AppendAnnouncement(sb, announcement);

            foreach (var ivr in ivrList)
                AppendIvrEntry(sb, ivr);

            foreach (var condition in timeConditionList)
                AppendTimeConditionEntry(sb, condition);

            // Extensions in a context are matched before anything it includes, so the phones and
            // feature codes above always win over a route pattern (D46).
            if (routeList.Count > 0)
            {
                sb.Append('\n');
                sb.Append("; Calls to the outside world, tried in the order the routes are listed\n");
                sb.Append($"include => {OutboundContext}\n");
            }

            AppendSetMohContext(sb, internalMusic);

            foreach (var ivr in ivrList)
                AppendIvrContext(sb, ivr, ivr.GreetingIn(announcementRows)!, enabled);

            foreach (var condition in timeConditionList)
                AppendTimeConditionContext(sb, condition, timezone);

            foreach (var trunk in trunkList)
                AppendTrunkContext(sb, trunk, inboundList.Where(r => r.TrunkID == trunk.TrunkID).ToList(), mohClassList);

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
        /// The time conditions that get a context, by play extension. Re-validated, so a row that
        /// reached the database another way cannot reach a conf file.
        ///
        /// Two things are needed, the same two a ring group needs: switched on, and a number to
        /// dial. A condition with no rules at all is still written — it is simply always closed,
        /// which is a state an admin can mean (D63).
        /// </summary>
        public static List<TimeCondition> TimeConditionRenderOrder(IEnumerable<TimeCondition> timeConditions)
        {
            var enabled = timeConditions.Where(t => t.Enabled).ToList();

            foreach (var condition in enabled)
            {
                var errors = condition.Validate();
                if (errors.Count > 0)
                    throw new InvalidOperationException($"Time condition '{condition.Name}' is invalid: {string.Join(" ", errors)}");
            }

            return enabled
                .Where(t => t.IsPlayable)
                .OrderBy(t => t.PlayExtension.Length)
                .ThenBy(t => t.PlayExtension, StringComparer.Ordinal)
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
        /// One entry per parking slot: dial the slot number from any phone and you pick up the
        /// call parked in it (D119). Nothing at all when parking is switched off.
        ///
        /// Written out one slot at a time rather than by letting res_parking generate them from a
        /// <c>parkext</c>, for the reason every other number in this context is written out one at
        /// a time: only numbers we generated can be dialled (D12, D46, D60). The alternative would
        /// be an <c>include</c> of the lot's own context, which is a context this file does not
        /// control the contents of.
        ///
        /// A single digit is safe here because everything else in this context is longer: an
        /// extension is 3 to 6 digits and every feature code starts with a star. Asterisk matches
        /// the whole extension, so 1 and 1001 are different numbers — but a phone dialling 1001
        /// digit by digit would match 1 first, which is why the slot count cannot grow past 9 and
        /// why slots are deliberately not the same shape as anything else people dial.
        /// </summary>
        private static void AppendParkingSlots(StringBuilder sb, ParkingSettings parking)
        {
            if (!parking.Enabled)
                return;

            var lot = ConfText.Safe(ParkingConfRenderer.LotName, "parking lot");

            sb.Append('\n');
            sb.Append($"; Call parking. Press {ConfText.Safe(parking.DtmfCode, "park feature code")} during a call to park it; the system says which\n");
            sb.Append("; slot it went into. Dial that slot number from any phone to pick it up.\n");
            sb.Append($"; An unclaimed call rings back the phone that parked it after {parking.TimeoutSeconds.ToString(CultureInfo.InvariantCulture)}s (D119).\n");
            sb.Append("; Each slot carries a hint as well, so a phone key can watch it: park:N@<context>\n");
            sb.Append("; is the device state res_parking publishes for the lot, and it is lit while a\n");
            sb.Append("; call is sitting there (D121).\n");

            var context = ConfText.Safe(ParkingConfRenderer.Context, "parking lot context");

            foreach (var slot in parking.SlotNumbers)
            {
                var number = slot.ToString(CultureInfo.InvariantCulture);

                sb.Append($"exten => {number},hint,park:{number}@{context}\n");
                sb.Append($"exten => {number},1,ParkedCall({lot},{number})\n");
                sb.Append(" same => n,Hangup()\n");
            }
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
        /// <param name="dialOptions">
        /// The same options an extension's own Dial carries, so that a member answering a group
        /// call gets the hold class on their channel exactly as they would have if the caller had
        /// dialled them directly (D122 amended).
        /// </param>
        private static void AppendRingGroup(StringBuilder sb, RingGroup group, List<Extension> enabledExtensions, string dialOptions)
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
                sb.Append($" same => n,Dial({string.Join("&", members)},{group.RingSeconds},{dialOptions})\n");
            }
            else
            {
                foreach (var member in members)
                    sb.Append($" same => n,Dial({member},{group.RingSeconds},{dialOptions})\n");
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
        /// The subroutine every internal Dial above Gosubs into on the channel it created, which
        /// is the other half of the hold music backfill (D122 amended).
        ///
        /// The caller's side is a line in their own extension, because their channel is the one
        /// executing dialplan. The channel the Dial creates executes none: chan_pjsip makes it,
        /// puts the endpoint's <c>moh_suggest</c> on it — res_pjsip's default for which is the
        /// literal <c>default</c> — and connects it. So when the caller is the one who presses
        /// hold, the channel Asterisk asks for music on is that one, it asks for a class called
        /// <c>default</c>, and this system deliberately defines no such class (D119): a warning in
        /// the log and silence on the line.
        ///
        /// <c>Dial</c>'s <c>U()</c> option is the hook Asterisk provides for this — it runs a
        /// Gosub on the called channel as it answers — and the subroutine does nothing the caller's
        /// line does not, guard included: it fills in only where Asterisk still has its own default
        /// sitting there. The <see cref="InternalMusicOnHoldLine"/> is shared, so the two sides
        /// cannot drift.
        ///
        /// Nothing at all when there is no class that ships, which is also why
        /// <see cref="InternalDialOptions"/> leaves the <c>U()</c> off the Dials then: a Gosub into
        /// a context that is not there fails the call rather than being ignored.
        /// </summary>
        private static void AppendSetMohContext(StringBuilder sb, string internalMusic)
        {
            if (internalMusic.Length == 0)
                return;

            sb.Append('\n');
            sb.Append($"[{SetMohContext}]\n");
            sb.Append("; Run by the U() option on every Dial above, on the channel that Dial creates, as\n");
            sb.Append("; it answers. That channel never executes dialplan of its own, so this is the only\n");
            sb.Append("; place a hold class can be named on it - and it is the channel that gets held when\n");
            sb.Append("; the caller is the one pressing hold. Same guard as the caller's own line: fill in\n");
            sb.Append("; only where Asterisk still has its own default sitting there (D122 amended).\n");
            sb.Append($"exten => s,1,{internalMusic}\n");
            sb.Append(" same => n,Return()\n");
        }

        /// <summary>
        /// One time condition's checks, in a context of its own (D63).
        ///
        /// A context per condition for the reason an IVR has one (D59): what is written here is a
        /// chain of labelled priorities, and labels belong to an extension. Like a menu's, this
        /// context <b>includes nothing</b>, so a call that arrived from outside can never fall
        /// through to anywhere that dials out.
        ///
        /// The order is holidays, then open hours, then the fall-through. A holiday wins over the
        /// weekly rules because that is what a holiday is for, and a call that matched nothing is
        /// closed by definition — which is why "closed" is the fall-through and needs no check of
        /// its own, and why a condition with no open hours is simply always closed.
        /// </summary>
        private static void AppendTimeConditionContext(StringBuilder sb, TimeCondition condition, string timezone)
        {
            var context = ConfText.Safe(condition.Context, "time condition context");
            var number = ConfText.Safe(condition.PlayExtension, "time condition play extension");
            var name = ConfText.Safe(condition.Name, "time condition name");
            var zone = ConfText.Safe(timezone, "system timezone");

            var holidays = condition.HolidayRules();
            var weekly = condition.WeeklyRules();

            sb.Append('\n');
            sb.Append($"[{context}]\n");
            sb.Append($"; {name}: the open/closed check, reached by the Goto on {number} in [{InternalContext}].\n");
            sb.Append("; Nothing is included here, so a caller can never fall through to a route out.\n");
            sb.Append($"; The hours below are LOCAL time in {zone}: every GotoIfTime names that zone as\n");
            sb.Append($"; its last argument, so Asterisk evaluates it there, daylight saving included.\n");
            sb.Append($"; The server's own clock is UTC and is not what these are matched against (D74).\n");

            // Answered before the check so that whatever is on the other side — an announcement, a
            // menu, a mailbox — starts on a channel that is already up. The cost, and it is worth
            // knowing: a caller sent on to an extension hears silence rather than ringback (D63).
            sb.Append("exten => s,1,Answer()\n");
            sb.Append($" same => n,NoOp(Time condition {number} {name})\n");

            if (holidays.Count > 0)
            {
                sb.Append('\n');
                sb.Append("; Holidays are checked first and win over the open hours. A GotoIfTime date\n");
                sb.Append("; has no year in it, so each of these comes round every year (D64).\n");

                for (var index = 0; index < holidays.Count; index++)
                {
                    var rule = holidays[index];
                    var label = rule.HasOverride
                        ? TimeConditionHolidayPrefix + (index + 1).ToString(CultureInfo.InvariantCulture)
                        : TimeConditionHolidayLabel;

                    sb.Append($" same => n,GotoIfTime(*,*,{DateFields(rule)},{zone}?{label})\n");
                }
            }

            if (weekly.Count > 0)
            {
                sb.Append('\n');
                sb.Append("; Open hours\n");

                foreach (var rule in weekly)
                {
                    var times = ConfText.Safe(rule.TimeRange(), "open hours");
                    var days = ConfText.Safe(rule.DaysField(), "open days");

                    sb.Append($" same => n,GotoIfTime({times},{days},*,*,{zone}?{TimeConditionOpenLabel})\n");
                }
            }
            else
            {
                sb.Append('\n');
                sb.Append("; No open hours are set, so this condition is always closed.\n");
            }

            AppendTimeConditionTarget(sb, number, TimeConditionClosedLabel, "closed", condition.ToClosedDestination());

            if (weekly.Count > 0)
                AppendTimeConditionTarget(sb, number, TimeConditionOpenLabel, "open", condition.ToOpenDestination());

            if (holidays.Any(r => !r.HasOverride))
                AppendTimeConditionTarget(sb, number, TimeConditionHolidayLabel, "holiday", condition.ToHolidayDestination());

            for (var index = 0; index < holidays.Count; index++)
            {
                var rule = holidays[index];
                if (!rule.HasOverride)
                    continue;

                var label = TimeConditionHolidayPrefix + (index + 1).ToString(CultureInfo.InvariantCulture);
                AppendTimeConditionTarget(sb, number, label, $"holiday {DateWords(rule)}", rule.ToDestination()!);
            }
        }

        /// <summary>
        /// The way into a time condition: one entry in the internal context, exactly as an
        /// announcement and an IVR get one (D57, D59). Dialling the number is the test button — it
        /// says which way the condition decides right now — and an inbound route, an IVR key or a
        /// ring group's failover arrive by the same door.
        /// </summary>
        private static void AppendTimeConditionEntry(StringBuilder sb, TimeCondition condition)
        {
            var number = ConfText.Safe(condition.PlayExtension, "time condition play extension");
            var name = ConfText.Safe(condition.Name, "time condition name");

            sb.Append('\n');
            sb.Append($"; {name} (time condition)\n");

            if (condition.Description.Length > 0)
                sb.Append($"; {ConfText.Safe(condition.Description, "time condition description")}\n");

            sb.Append($"exten => {number},1,Goto({ConfText.Safe(condition.Context, "time condition context")},s,1)\n");
        }

        /// <summary>
        /// One labelled end of a time condition: a NoOp saying which way the clock went, and then
        /// the destination itself through the shared helper (D36). An empty destination is a
        /// Hangup, because a call has to end somewhere.
        /// </summary>
        private static void AppendTimeConditionTarget(StringBuilder sb, string number, string label, string what, Destination destination)
        {
            sb.Append('\n');
            sb.Append($" same => n({ConfText.Safe(label, "time condition label")}),NoOp(Time condition {number} {ConfText.Safe(what, "time condition case")} to {ConfText.Safe(destination.Key, "destination")})\n");
            sb.Append(DestinationDialplan.Lines(destination));
        }

        /// <summary>
        /// The day-of-month and month fields of a GotoIfTime spec, e.g. "25,dec". The year the rule
        /// was written with is not among them: the dialplan has nowhere to put one (D64).
        /// </summary>
        private static string DateFields(TimeConditionRule rule) =>
            $"{ConfText.Safe(rule.DayOfMonthField(), "holiday day")},{ConfText.Safe(rule.MonthField(), "holiday month")}";

        /// <summary>The same date the way a comment should read it: "25 dec".</summary>
        private static string DateWords(TimeConditionRule rule) =>
            $"{ConfText.Safe(rule.DayOfMonthField(), "holiday day")} {ConfText.Safe(rule.MonthField(), "holiday month")}";

        /// <summary>
        /// Where calls from one provider land: one entry per DID, sent on by the shared
        /// destination helper (D36), and one entry for everything else — the trunk's catch-all
        /// route if it has one, otherwise a hangup (D50).
        ///
        /// No includes and no ordering tricks are needed here, unlike outbound (D46): the DIDs are
        /// literal extensions and the catch-all is a pattern, and Asterisk always prefers a literal
        /// match to a pattern.
        ///
        /// A route that names a music on hold class sets it on the channel before the call is
        /// handed on, so it is already there whenever somebody holds this caller (D122 amended).
        /// </summary>
        private static void AppendTrunkContext(StringBuilder sb, Trunk trunk, List<InboundRoute> routes, List<MohClass> mohClasses)
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
                sb.Append(MusicOnHoldLine(catchAll, mohClasses));
                sb.Append(DestinationDialplan.Lines(destination));
            }

            // The route targets: each is a labeled jump destination for its GotoIf above.
            foreach (var route in routes.Where(r => !r.CatchAll))
            {
                var number = ConfText.Safe(route.DialplanExtension, "DID");
                var destination = route.ToDestination();
                var label = $"r{route.InboundRouteID}";

                sb.Append($" same => n({label}),NoOp(Inbound {number} on {trunkName} to {ConfText.Safe(destination.Key, "destination")})\n");
                sb.Append(MusicOnHoldLine(route, mohClasses));
                sb.Append(DestinationDialplan.Lines(destination));
            }
        }

        /// <summary>
        /// The class that ships, or null when the renderer was given no classes at all. The same
        /// one <c>MohClassRepository.Default()</c> picks — flagged, lowest ID first — read off the
        /// list this renderer is already handed rather than the database, because a renderer is a
        /// pure function of what it is given.
        /// </summary>
        private static MohClass? DefaultOf(List<MohClass> mohClasses) =>
            mohClasses.Where(c => c.IsDefault).OrderBy(c => c.MohClassID).FirstOrDefault();

        /// <summary>
        /// The options a Dial to a phone in this building carries. The shared
        /// <see cref="DialOptions"/> plus the Gosub that names the hold class on the channel the
        /// Dial creates (D122 amended) — and that one only when there is a
        /// <see cref="SetMohContext"/> to Gosub into, because Asterisk fails a call whose
        /// <c>U()</c> names a context it cannot find rather than carrying on without it.
        ///
        /// A Dial out over a trunk keeps the bare <see cref="DialOptions"/>: the channel it creates
        /// belongs to the provider, and what the far end of an outbound call hears on hold is not
        /// this system's to choose.
        /// </summary>
        private static string InternalDialOptions(bool hasSetMohContext) =>
            hasSetMohContext ? $"{DialOptions}U({SetMohContext})" : DialOptions;

        /// <summary>
        /// What a caller whose call started on a phone here hears while they are held, as the one
        /// application that says it (D122 amended). Written both on the caller's own channel, one
        /// priority ahead of their Dial, and on the channel that Dial creates, through the
        /// <see cref="SetMohContext"/> subroutine — the same text in both places, so the two sides
        /// of a call cannot end up guarded differently. Empty when there is no class that ships, which
        /// leaves those calls in the silence they were in before this existed.
        ///
        /// The guard is the whole point. The class is named <b>only when Asterisk still has its own
        /// default on the channel</b>, so a class an inbound route already chose for this caller
        /// survives the <c>Goto</c> into the extension they were routed to: the route's choice is
        /// the caller's, and internal only backfills where nobody chose. Two values count as "still
        /// the default" and both are tested, because <c>CHANNEL(musicclass)</c> is not empty on the
        /// channels this actually runs on: chan_pjsip puts the endpoint's <c>moh_suggest</c> on
        /// every channel it creates and res_pjsip's default for that is the literal
        /// <c>default</c> — so that is the test that fires, and the empty one is for a channel that
        /// arrived any other way. Neither names a class this system ever writes (D119), so both
        /// mean silence and both are ours to fill in.
        ///
        /// <c>ExecIf</c> rather than a plain <c>Set</c>, and one priority of its own ahead of the
        /// Dial: the alternative is a labelled <c>GotoIf</c> chain, which is three lines and a
        /// label per extension to save one module on the allowlist (D31).
        /// </summary>
        private static string InternalMusicOnHoldLine(MohClass? mohClass)
        {
            if (mohClass == null)
                return "";

            if (!MohClass.IsValidName(mohClass.Name))
                throw new InvalidOperationException($"The music on hold class that ships, '{mohClass.Name}', is not a name Asterisk could match.");

            var name = ConfText.Safe(mohClass.Name.Trim(), "music on hold class");
            var current = "${CHANNEL(musicclass)}";

            return $"ExecIf($[\"{current}\" = \"\" | \"{current}\" = \"{MohClass.ReservedName}\"]?Set(CHANNEL(musicclass)={name}))";
        }

        /// <summary>
        /// What this route's caller hears while anybody has them on hold, as the one dialplan line
        /// that says it (D122 amended): the class is set on the channel here, before the call is
        /// handed on, so it is already in place whoever holds them and wherever the call has been
        /// transferred to by then.
        ///
        /// <c>CHANNEL(musicclass)</c> rather than a <c>MusicOnHold</c> of our own: holding is the
        /// far end's doing, and what Asterisk needs from us is the name to reach for when it
        /// happens. It is also the name Asterisk's own function has —
        /// <c>ast_channel_musicclass</c>, <c>func_channel.c</c> — which is worth writing down
        /// because "mohclass" is what everyone calls it in conversation and Asterisk would answer
        /// that with a warning and no music.
        ///
        /// A route that names no class writes no line, which leaves the channel exactly as every
        /// route left it before this existed. The name is re-validated and passed through
        /// <see cref="ConfText.Safe"/> like every other value that reaches a conf file, and a class
        /// this renderer was not given is refused rather than guessed at.
        /// </summary>
        private static string MusicOnHoldLine(InboundRoute route, List<MohClass> mohClasses)
        {
            if (route.MohClassID is not { } mohClassID)
                return "";

            var what = route.CatchAll ? "catch-all" : route.DID;
            var mohClass = mohClasses.FirstOrDefault(c => c.MohClassID == mohClassID);

            if (mohClass == null)
                throw new InvalidOperationException($"Inbound route '{what}' plays music on hold class {mohClassID}, which the renderer was not given.");

            if (!MohClass.IsValidName(mohClass.Name))
                throw new InvalidOperationException($"Inbound route '{what}' plays music on hold class '{mohClass.Name}', which is not a name Asterisk could match.");

            var name = ConfText.Safe(mohClass.Name.Trim(), "music on hold class");

            return $" same => n,Set(CHANNEL(musicclass)={name})\n";
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
                var prepend = ConfText.Safe(route.PrependDigits, "prepend digits");
                var trunkName = ConfText.Safe(trunk.Name, "trunk name");
                var trunkHost = ConfText.Safe(trunk.ServerHost, "trunk server host");

                // What the trunk is given: the prepend in front of the dialled digits with the
                // stripped ones dropped (D109). Strip 0 + empty prepend is plain ${EXTEN}, which
                // is what the file said before either field existed.
                var sent = route.StripDigits > 0 ? $"${{EXTEN:{route.StripDigits}}}" : "${EXTEN}";

                sb.Append('\n');
                sb.Append($"[{ConfText.Safe(route.Context, "route context")}]\n");
                sb.Append($"; {name} ({route.Priority}) out over {trunkName}\n");
                // The full URI form is required: chan_pjsip treats a bare dialstring as a literal
                // URI and rejects it ("Could not create dialog to invalid URI").
                sb.Append($"exten => {pattern},1,Dial(PJSIP/{trunkName}/sip:{prepend}{sent}@{trunkHost},{OutboundRingSeconds},{DialOptions})\n");
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
