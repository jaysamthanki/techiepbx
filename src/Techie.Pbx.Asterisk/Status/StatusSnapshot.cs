using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Status
{
    /// <summary>
    /// Everything <see cref="AttentionRules.Evaluate"/> needs, already loaded. The caller does the
    /// reading — repositories, AMI, the marker files, the disk — and this carries the answers, so
    /// the rules themselves stay a pure function that a test can hand any system it likes.
    ///
    /// Every list is every row, enabled or not: several of the rules are about something being
    /// switched off, so filtering here would hide the very thing they look for.
    /// </summary>
    public class StatusSnapshot
    {
        /// <summary>Whether Asterisk answered at all. When it did not, every state below is a guess.</summary>
        public bool AmiReachable { get; set; }

        public List<Announcement> Announcements { get; set; } = new();
        public List<Certificate> Certificates { get; set; } = new();

        /// <summary>The database has changed and Asterisk has not been given it yet (D26).</summary>
        public bool ConfigPending { get; set; }

        /// <summary>How full the disks holding the database and the announcement audio are.</summary>
        public List<DiskUsage> Disks { get; set; } = new();

        public List<Extension> Extensions { get; set; } = new();
        public List<InboundRoute> InboundRoutes { get; set; } = new();
        public List<Ivr> Ivrs { get; set; } = new();
        public List<OutboundRoute> OutboundRoutes { get; set; } = new();

        /// <summary>
        /// The keys on every phone, which is where a phone's registration lives since schema 020:
        /// each row carries its own PhoneID, so the rules match them back to their phone.
        /// </summary>
        public List<PhoneButton> PhoneButtons { get; set; } = new();

        public List<Phone> Phones { get; set; } = new();

        /// <summary>Asterisk is still running config an apply has replaced on disk (D104).</summary>
        public bool RestartRequired { get; set; }

        public List<RingGroup> RingGroups { get; set; } = new();
        public List<TimeCondition> TimeConditions { get; set; } = new();

        /// <summary>The System.Timezone setting as stored, or empty when nobody has set one.</summary>
        public string Timezone { get; set; } = "";

        /// <summary>Registration state by trunk name, as <c>RegistrationStatus.MapTrunks</c> reads it.</summary>
        public Dictionary<string, RegistrationState> TrunkStates { get; set; } = new(StringComparer.Ordinal);

        public List<Trunk> Trunks { get; set; } = new();
    }
}
