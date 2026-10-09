using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Migration
{
    /// <summary>
    /// What this TNPBX already has that an import could collide with. A snapshot rather than live
    /// reads, so the planner is a function of its inputs and the tests can hand it one.
    /// </summary>
    public class ExistingConfig
    {
        public HashSet<string> AnnouncementNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Extensions a phone already registers as (schema 020): another phone may not.</summary>
        public HashSet<string> ClaimedLines { get; set; } = new(StringComparer.Ordinal);

        public HashSet<string> ExtensionNumbers { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> OutboundRouteNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> PhoneMacs { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> TrunkNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static ExistingConfig FromDatabase(Database database) => new()
        {
            AnnouncementNames = new(new AnnouncementRepository(database).GetAll().Select(a => a.Name), StringComparer.OrdinalIgnoreCase),
            ClaimedLines = new(new PhoneButtonRepository(database).GetLines().Select(b => b.TargetValue), StringComparer.Ordinal),
            ExtensionNumbers = new(new ExtensionRepository(database).GetAll().Select(e => e.Number), StringComparer.Ordinal),
            OutboundRouteNames = new(new OutboundRouteRepository(database).GetAll().Select(r => r.Name), StringComparer.OrdinalIgnoreCase),
            PhoneMacs = new(new PhoneRepository(database).GetAll().Select(p => p.Mac), StringComparer.Ordinal),
            TrunkNames = new(new TrunkRepository(database).GetAll().Select(t => t.Name), StringComparer.OrdinalIgnoreCase),
        };
    }
}
