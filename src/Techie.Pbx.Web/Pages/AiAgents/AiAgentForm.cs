using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.AiAgents
{
    /// <summary>
    /// What the create/edit form in the modal shows, and what it posts back. The text fields are
    /// nullable because model binding turns a field the user left blank into null whatever the
    /// initialiser says.
    /// </summary>
    public class AiAgentForm
    {
        public long AiAgentID { get; set; }
        public bool Enabled { get; set; } = true;
        public List<string> Errors { get; set; } = new();

        /// <summary>Every enabled extension the agent could hand a caller to. Not posted back.</summary>
        public List<Extension> Extensions { get; set; } = new();

        public string? Greeting { get; set; } = "";
        public bool IsNew => this.AiAgentID == 0;
        public string? Model { get; set; } = "";
        public string? Name { get; set; } = "";
        public string? Number { get; set; } = "";
        public string? Prompt { get; set; } = "";
        public string? TransferTo { get; set; } = "";
        public string? VoiceId { get; set; } = "";
    }
}
