namespace Techie.Pbx.Core.Migration
{
    /// <summary>One mailbox's messages the import will copy into the voicemail spool.</summary>
    public class PlannedMailbox
    {
        /// <summary>Relative to <see cref="SourceDirectory"/>: <c>INBOX/msg0000.wav</c>, <c>Old/msg0003.txt</c>.</summary>
        public List<string> Files { get; set; } = new();

        public string Mailbox { get; set; } = "";

        /// <summary>How many messages: one per <c>.txt</c> envelope, which is how app_voicemail counts them.</summary>
        public int Messages => this.Files.Count(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

        /// <summary><c>files/voicemail/&lt;ext&gt;</c> in the staging directory.</summary>
        public string SourceDirectory { get; set; } = "";
    }
}
