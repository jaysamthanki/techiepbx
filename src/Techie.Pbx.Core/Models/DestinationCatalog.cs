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
        public const string ExtensionsGroup = "Extensions";
        public const string OtherGroup = "Other";
        public const string RingGroupsGroup = "Ring groups";
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

        public static List<DestinationChoice> All(IEnumerable<Extension> extensions, IEnumerable<RingGroup> ringGroups)
        {
            var usable = InNumberOrder(extensions);
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

            choices.Add(new DestinationChoice
            {
                Destination = Destination.Hangup,
                GroupName = OtherGroup,
                Label = "Hang up",
            });

            return choices;
        }

        /// <summary>
        /// The catalog entry a stored destination points at, or null when it points at something
        /// that has since been deleted, disabled or had its voicemail switched off. Callers use it
        /// both to label a choice and to notice a dangling one.
        /// </summary>
        public static DestinationChoice? Find(IEnumerable<Extension> extensions, Destination? destination) =>
            Find(extensions, new List<RingGroup>(), destination);

        public static DestinationChoice? Find(IEnumerable<Extension> extensions, IEnumerable<RingGroup> ringGroups, Destination? destination)
        {
            if (destination == null)
                return null;

            return All(extensions, ringGroups)
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
