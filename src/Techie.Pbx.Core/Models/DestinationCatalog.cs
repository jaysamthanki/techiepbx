namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// Every place a call can be sent right now, worked out from what exists rather than stored
    /// anywhere (D35). Pure function over rows the caller has already loaded, like the renderers:
    /// no database, no I/O, nothing to keep in step.
    ///
    /// As features arrive, each adds its own source to <see cref="All"/> — ring groups, IVRs,
    /// trunks — and every picker in the UI grows the new entries without being touched.
    /// </summary>
    public static class DestinationCatalog
    {
        public const string AnnouncementsGroup = "Announcements";
        public const string CallFlowControlsGroup = "Call flow controls";
        public const string ExtensionsGroup = "Extensions";
        public const string IvrsGroup = "IVRs";
        public const string OtherGroup = "Other";
        public const string RingGroupsGroup = "Ring groups";
        public const string TimeConditionsGroup = "Time conditions";
        public const string VoicemailGroup = "Voicemail";

        /// <summary>
        /// The whole list, in the order a picker should show it: extensions by number, then the
        /// mailboxes that actually exist, then the things that are always available.
        ///
        /// Disabled extensions are left out: they are not in the generated config, so a call sent
        /// to one would go nowhere. An extension without voicemail switched on has no mailbox
        /// entry, for the same reason.
        /// </summary>
        public static List<DestinationChoice> All(IEnumerable<Extension> extensions) =>
            All(extensions, new List<RingGroup>());

        public static List<DestinationChoice> All(IEnumerable<Extension> extensions, IEnumerable<RingGroup> ringGroups) =>
            All(extensions, ringGroups, new List<Announcement>());

        public static List<DestinationChoice> All(
            IEnumerable<Extension> extensions,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements) =>
            All(extensions, ringGroups, announcements, new List<Ivr>());

        public static List<DestinationChoice> All(
            IEnumerable<Extension> extensions,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements,
            IEnumerable<Ivr> ivrs) =>
            All(extensions, ringGroups, announcements, ivrs, new List<TimeCondition>());

        public static List<DestinationChoice> All(
            IEnumerable<Extension> extensions,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements,
            IEnumerable<Ivr> ivrs,
            IEnumerable<TimeCondition> timeConditions) =>
            All(extensions, ringGroups, announcements, ivrs, timeConditions, new List<CallFlowControl>());

        public static List<DestinationChoice> All(
            IEnumerable<Extension> extensions,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements,
            IEnumerable<Ivr> ivrs,
            IEnumerable<TimeCondition> timeConditions,
            IEnumerable<CallFlowControl> callFlowControls)
        {
            var usable = InNumberOrder(extensions);
            var announcementList = announcements.ToList();
            var choices = new List<DestinationChoice>();

            foreach (var extension in usable)
            {
                choices.Add(new DestinationChoice
                {
                    Destination = new Destination(DestinationType.Extension, extension.Number),
                    GroupName = ExtensionsGroup,
                    Label = $"{extension.Number} {extension.Name}",
                });
            }

            foreach (var extension in usable.Where(e => e.VoicemailEnabled))
            {
                choices.Add(new DestinationChoice
                {
                    Destination = new Destination(DestinationType.Voicemail, extension.Number),
                    GroupName = VoicemailGroup,
                    Label = $"{extension.Number} {extension.Name}",
                });
            }

            // Disabled groups are left out for the same reason disabled extensions are: they are
            // not in the generated dialplan, so a call sent to one would go nowhere (D54).
            foreach (var group in ringGroups.Where(g => g.Enabled).OrderBy(g => g.Number.Length).ThenBy(g => g.Number, StringComparer.Ordinal))
            {
                choices.Add(new DestinationChoice
                {
                    Destination = new Destination(DestinationType.RingGroup, group.Number),
                    GroupName = RingGroupsGroup,
                    Label = $"{group.Number} {group.Name}",
                });
            }

            // Only the ones a call can actually reach: switched on, with audio uploaded, and with
            // a play extension to Goto. Without all three there is nothing to send a call to, so
            // offering it would be offering a dead end (D56).
            foreach (var announcement in announcementList
                .Where(a => a.IsPlayable)
                .OrderBy(a => a.PlayExtension.Length)
                .ThenBy(a => a.PlayExtension, StringComparer.Ordinal))
            {
                choices.Add(new DestinationChoice
                {
                    Destination = announcement.ToDestination(),
                    GroupName = AnnouncementsGroup,
                    Label = $"{announcement.PlayExtension} {announcement.Name}",
                });
            }

            // Same three conditions as an announcement's, plus the greeting: an IVR whose
            // announcement is gone, switched off or has no audio yet is not in the dialplan
            // either, so sending a call to it would be sending it nowhere (D58).
            foreach (var ivr in ivrs
                .Where(i => i.IsPlayable && i.GreetingIn(announcementList) != null)
                .OrderBy(i => i.PlayExtension.Length)
                .ThenBy(i => i.PlayExtension, StringComparer.Ordinal))
            {
                choices.Add(new DestinationChoice
                {
                    Destination = ivr.ToDestination(),
                    GroupName = IvrsGroup,
                    Label = $"{ivr.PlayExtension} {ivr.Name}",
                });
            }

            // A time condition needs the same two things a ring group does — switched on, and a
            // number to Goto — and nothing else: what it decides is the clock's business, and a
            // condition with no rules at all is simply always closed (D63).
            foreach (var condition in timeConditions
                .Where(t => t.IsPlayable)
                .OrderBy(t => t.PlayExtension.Length)
                .ThenBy(t => t.PlayExtension, StringComparer.Ordinal))
            {
                choices.Add(new DestinationChoice
                {
                    Destination = condition.ToDestination(),
                    GroupName = TimeConditionsGroup,
                    Label = $"{condition.PlayExtension} {condition.Name}",
                });
            }

            // A call flow control has no switch of its own to turn it off and always has a code,
            // so every one is somewhere a call can go (F9). By code, the way the others go by number.
            foreach (var control in callFlowControls
                .OrderBy(c => c.FeatureCode.Length)
                .ThenBy(c => c.FeatureCode, StringComparer.Ordinal))
            {
                choices.Add(new DestinationChoice
                {
                    Destination = control.ToDestination(),
                    GroupName = CallFlowControlsGroup,
                    Label = $"{control.FeatureCode} {control.Name}",
                });
            }

            choices.Add(new DestinationChoice
            {
                Destination = Destination.Hangup,
                GroupName = OtherGroup,
                Label = "Hang up",
            });

            return choices;
        }

        /// <summary>
        /// The catalog without the one entity being edited, for the pickers where pointing at
        /// yourself would be a loop: a ring group's failover (D54), an IVR's final destination
        /// (D59), a time condition's three cases (D63). Everything else stays, including others
        /// of the same kind, because handing on to another group, menu or condition is a feature.
        /// A null <paramref name="self"/> — a new entity, not saved yet — leaves the list whole.
        /// </summary>
        public static List<DestinationChoice> Except(IEnumerable<DestinationChoice> choices, Destination? self) =>
            choices
                .Where(c => self == null || !string.Equals(c.Destination.Key, self.Key, StringComparison.Ordinal))
                .ToList();

        /// <summary>
        /// The catalog entry a stored destination points at, or null when it points at something
        /// that has since been deleted, disabled or had its voicemail switched off. Callers use it
        /// both to label a choice and to notice a dangling one.
        /// </summary>
        public static DestinationChoice? Find(IEnumerable<Extension> extensions, Destination? destination) =>
            Find(extensions, new List<RingGroup>(), destination);

        public static DestinationChoice? Find(IEnumerable<Extension> extensions, IEnumerable<RingGroup> ringGroups, Destination? destination) =>
            Find(extensions, ringGroups, new List<Announcement>(), destination);

        public static DestinationChoice? Find(
            IEnumerable<Extension> extensions,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements,
            Destination? destination) =>
            Find(extensions, ringGroups, announcements, new List<Ivr>(), destination);

        public static DestinationChoice? Find(
            IEnumerable<Extension> extensions,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements,
            IEnumerable<Ivr> ivrs,
            Destination? destination) =>
            Find(extensions, ringGroups, announcements, ivrs, new List<TimeCondition>(), destination);

        public static DestinationChoice? Find(
            IEnumerable<Extension> extensions,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements,
            IEnumerable<Ivr> ivrs,
            IEnumerable<TimeCondition> timeConditions,
            Destination? destination) =>
            Find(extensions, ringGroups, announcements, ivrs, timeConditions, new List<CallFlowControl>(), destination);

        public static DestinationChoice? Find(
            IEnumerable<Extension> extensions,
            IEnumerable<RingGroup> ringGroups,
            IEnumerable<Announcement> announcements,
            IEnumerable<Ivr> ivrs,
            IEnumerable<TimeCondition> timeConditions,
            IEnumerable<CallFlowControl> callFlowControls,
            Destination? destination)
        {
            if (destination == null)
                return null;

            return All(extensions, ringGroups, announcements, ivrs, timeConditions, callFlowControls)
                .FirstOrDefault(c => string.Equals(c.Destination.Key, destination.Key, StringComparison.Ordinal));
        }

        /// <summary>
        /// Enabled extensions, shortest number first then in order, which is how the dialplan and
        /// the extensions table already sort them.
        /// </summary>
        private static List<Extension> InNumberOrder(IEnumerable<Extension> extensions) =>
            extensions
                .Where(e => e.Enabled)
                .OrderBy(e => e.Number.Length)
                .ThenBy(e => e.Number, StringComparer.Ordinal)
                .ToList();
    }
}
