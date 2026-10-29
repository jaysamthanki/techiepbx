namespace Techie.Pbx.Web.Pages.AiAgents
{
    /// <summary>One line of the AI agents table.</summary>
    public class AiAgentRow
    {
        public long AiAgentID { get; set; }
        public bool Enabled { get; set; }

        /// <summary>The model slug, or "site default" when the agent names none.</summary>
        public string Model { get; set; } = "";

        public string Name { get; set; } = "";
        public string Number { get; set; } = "";

        /// <summary>The handoff extension, or "none".</summary>
        public string TransferTo { get; set; } = "";

        /// <summary>The voice ID, or "site default" when the agent names none.</summary>
        public string VoiceId { get; set; } = "";
    }
}
