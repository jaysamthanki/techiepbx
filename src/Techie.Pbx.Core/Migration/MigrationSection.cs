namespace Techie.Pbx.Core.Migration
{
    /// <summary>The parts of an import, in the order they land, as the preview and report group them.</summary>
    public static class MigrationSection
    {
        public const string Apply = "Apply";
        public const string Announcements = "Announcements";
        public const string Export = "Export";
        public const string Extensions = "Extensions";
        public const string InboundRoutes = "Inbound routes";
        public const string OutboundRoutes = "Outbound routes";
        public const string Phones = "Phones";
        public const string Sounds = "Sounds";
        public const string Trunks = "Trunks";
        public const string Voicemail = "Voicemail";

        /// <summary>The sections in the order an import writes them, which is the order warnings are listed in.</summary>
        public static readonly IReadOnlyList<string> InOrder = new[]
        {
            Export, Extensions, Trunks, OutboundRoutes, InboundRoutes, Sounds, Phones, Voicemail, Apply,
        };

        /// <summary>Warnings grouped by section, in import order, keeping their order within each.</summary>
        public static List<MigrationWarning> Ordered(IEnumerable<MigrationWarning> warnings) =>
            warnings.OrderBy(w => InOrder.Contains(w.Section) ? InOrder.ToList().IndexOf(w.Section) : InOrder.Count).ToList();
    }
}
