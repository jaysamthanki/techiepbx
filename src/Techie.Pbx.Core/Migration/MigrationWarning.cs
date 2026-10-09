namespace Techie.Pbx.Core.Migration
{
    /// <summary>
    /// One thing the import could not carry over as it was, said three ways: what happened, why,
    /// and what the operator has to do about it by hand. A migration that half-lands and says why
    /// beats one that refuses (docs/freepbx-import.md), so every skip and every change the
    /// importer makes to a value is one of these rather than a line in a log.
    /// </summary>
    public class MigrationWarning
    {
        /// <summary>What the operator has to do by hand, or empty when nothing.</summary>
        public string Action { get; set; } = "";

        /// <summary>Which part of the import this is about: a <see cref="MigrationSection"/> value.</summary>
        public string Section { get; set; } = "";

        /// <summary>What happened, naming the row: "Extension 390 was given a new secret."</summary>
        public string What { get; set; } = "";

        /// <summary>Why it happened.</summary>
        public string Why { get; set; } = "";

        public MigrationWarning()
        {
        }

        public MigrationWarning(string section, string what, string why, string action)
        {
            this.Action = action;
            this.Section = section;
            this.What = what;
            this.Why = why;
        }
    }
}
