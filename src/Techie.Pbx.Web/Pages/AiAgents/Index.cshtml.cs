using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.AiAgents
{
    /// <summary>
    /// The AI agents page: a number that answers with a voice and talks from its own prompt (F11,
    /// D199). Built like the other list pages — a shell htmx fills, forms in the shared Bootstrap
    /// modal (D42), rows that open their own edit form (D48). No key or secret is shown here.
    /// </summary>
    public class IndexModel : PageModel
    {
        private const string SiteDefault = "site default";

        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly AiAgentRepository aiAgents;
        private readonly ExtensionRepository extensions;

        public IndexModel()
        {
            this.aiAgents = new AiAgentRepository(PbxDatabase.Current);
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>The create or edit form, which the page shows in the Bootstrap modal.</summary>
        public IActionResult OnGetForm(long? aiAgentID)
        {
            if (aiAgentID is null or 0)
                return this.Partial("_Form", this.Fill(new AiAgentForm()));

            var agent = this.aiAgents.GetByID(aiAgentID.Value);
            if (agent == null)
                return this.NotFound();

            return this.Partial("_Form", this.Fill(new AiAgentForm
            {
                AiAgentID = agent.AiAgentID,
                Enabled = agent.Enabled,
                Greeting = agent.Greeting,
                Model = agent.Model,
                Name = agent.Name,
                Number = agent.Number,
                Prompt = agent.Prompt,
                TransferTo = agent.TransferTo,
                VoiceId = agent.VoiceId,
            }));
        }

        /// <summary>The whole table, by number.</summary>
        public PartialViewResult OnGetTable()
        {
            var rows = this.aiAgents.GetAll()
                .Select(agent => new AiAgentRow
                {
                    AiAgentID = agent.AiAgentID,
                    Enabled = agent.Enabled,
                    Model = agent.Model.Length > 0 ? agent.Model : SiteDefault,
                    Name = agent.Name,
                    Number = agent.Number,
                    TransferTo = agent.TransferTo.Length > 0 ? agent.TransferTo : "none",
                    VoiceId = agent.VoiceId.Length > 0 ? agent.VoiceId : SiteDefault,
                })
                .ToList();

            return this.Partial("_Table", rows);
        }

        public IActionResult OnPostDelete(long aiAgentID)
        {
            var agent = this.aiAgents.GetByID(aiAgentID);
            if (agent == null)
                return this.NotFound();

            this.aiAgents.Delete(aiAgentID);

            Log.Info($"AI agent {agent.Number} deleted by {this.User.Identity?.Name}");
            return this.Changed($"AI agent {agent.Number} deleted.");
        }

        /// <summary>
        /// Creates or updates one agent. Validation failures come back as the form again, with the
        /// repository's messages on it.
        /// </summary>
        public IActionResult OnPostSave(AiAgentForm form)
        {
            var isNew = form.AiAgentID == 0;
            var agent = isNew ? new AiAgent() : this.aiAgents.GetByID(form.AiAgentID);
            if (agent == null)
                return this.NotFound();

            agent.Enabled = form.Enabled;
            agent.Greeting = Text(form.Greeting);
            agent.Model = Text(form.Model);
            agent.Name = Text(form.Name);
            agent.Number = Text(form.Number);
            agent.Prompt = Text(form.Prompt);
            agent.TransferTo = Text(form.TransferTo);
            agent.VoiceId = Text(form.VoiceId);

            try
            {
                if (isNew)
                    this.aiAgents.Insert(agent);
                else
                    this.aiAgents.Update(agent);
            }
            catch (ValidationFailedException ex)
            {
                form.Errors = ex.Errors.ToList();
                return this.Partial("_Form", this.Fill(form));
            }

            // The prompt is deliberately not logged: it can carry business detail nobody asked
            // to have in a log file.
            Log.Info($"AI agent {agent.Number} {(isNew ? "created" : "updated")} by {this.User.Identity?.Name}");
            return this.Changed($"AI agent {agent.Number} saved.");
        }

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to.
        /// "aiAgentsChanged" refreshes the table, "configChanged" wakes the navbar's apply button
        /// (D43), "pbxToast" says what happened.
        /// </summary>
        private IActionResult Changed(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["aiAgentsChanged"] = null,
                ["configChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>The list the form cannot know for itself: the extensions a caller can be handed to.</summary>
        private AiAgentForm Fill(AiAgentForm form)
        {
            form.Extensions = this.extensions.GetAll().Where(e => e.Enabled).ToList();
            return form;
        }
    }
}
