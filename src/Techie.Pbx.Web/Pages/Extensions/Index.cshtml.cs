using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Security;
using Techie.Pbx.Web.Pages.Shared;

namespace Techie.Pbx.Web.Pages.Extensions
{
    /// <summary>
    /// The extensions page. The page itself is a shell: htmx fetches the table, the create/edit
    /// form and the registration badges from the handlers here as HTML partials. Anything that
    /// changes data answers 204 with an HX-Trigger header, and the page reacts to those events.
    /// </summary>
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly ExtensionRepository extensions;
        private readonly ConfigPendingMarker pending;
        private readonly SettingsRepository settings;

        /// <summary>Whether the database has changed since the last apply, for the page shell.</summary>
        public bool ConfigPending { get; private set; }

        public IndexModel()
        {
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.pending = new ConfigPendingMarker(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
            this.ConfigPending = this.pending.IsPending;
        }

        /// <summary>The create or edit form, which the page shows in a sweetalert2 modal.</summary>
        public IActionResult OnGetForm(long? extensionID)
        {
            if (extensionID is null or 0)
            {
                // A new extension gets its password and a voicemail PIN now, so that nobody has
                // to invent either and settles on 1234.
                return this.Partial("_Form", new ExtensionForm
                {
                    Secret = SecretGenerator.Create(),
                    VoicemailPin = SecretGenerator.CreatePin(),
                });
            }

            var extension = this.extensions.GetByID(extensionID.Value);
            if (extension == null)
                return this.NotFound();

            return this.Partial("_Form", new ExtensionForm
            {
                Enabled = extension.Enabled,
                ExtensionID = extension.ExtensionID,
                Name = extension.Name,
                Number = extension.Number,
                VoicemailAttachRecording = extension.VoicemailAttachRecording,
                VoicemailDeleteAfterEmail = extension.VoicemailDeleteAfterEmail,
                VoicemailEmail = extension.VoicemailEmail,
                VoicemailEnabled = extension.VoicemailEnabled,
                VoicemailPin = extension.VoicemailPin,
            });
        }

        /// <summary>
        /// The "apply is due" banner, which asks the marker file rather than remembering anything
        /// in the browser: reload the page, or open a second one, and the answer is the same (D26).
        /// </summary>
        public PartialViewResult OnGetPending() => this.Partial("_ApplyPending", this.pending.IsPending);

        /// <summary>
        /// The five second poll: one badge per extension, each marked for an out-of-band swap so
        /// htmx drops it into the row it belongs to without touching the rest of the table.
        /// </summary>
        public PartialViewResult OnGetStatus()
        {
            var numbers = this.extensions.GetAll().Select(e => e.Number).ToList();
            var states = RegistrationStatus.Read(this.Ami(), numbers);

            var badges = numbers
                .Select(number => StatusBadge.ForExtension(number, states[number], outOfBand: true))
                .ToList();

            return this.Partial("_Status", badges);
        }

        /// <summary>The whole table, including current registration state so a refresh is not blank.</summary>
        public PartialViewResult OnGetTable()
        {
            var all = this.extensions.GetAll();
            var states = RegistrationStatus.Read(this.Ami(), all.Select(e => e.Number));

            var rows = all
                .Select(extension => new ExtensionRow
                {
                    Enabled = extension.Enabled,
                    ExtensionID = extension.ExtensionID,
                    Name = extension.Name,
                    Number = extension.Number,
                    State = states[extension.Number],
                })
                .ToList();

            return this.Partial("_Table", rows);
        }

        public IActionResult OnPostDelete(long extensionID)
        {
            var extension = this.extensions.GetByID(extensionID);
            if (extension == null)
                return this.NotFound();

            this.extensions.Delete(extensionID);

            Log.Info($"Extension {extension.Number} deleted by {this.User.Identity?.Name}");
            return this.Changed($"Extension {extension.Number} deleted.");
        }

        /// <summary>
        /// Creates or updates one extension. Validation failures come back as the form again,
        /// with the repository's messages on it, which htmx swaps into the open modal.
        /// </summary>
        public IActionResult OnPostSave(ExtensionForm form)
        {
            var isNew = form.ExtensionID == 0;
            var extension = isNew ? new Extension() : this.extensions.GetByID(form.ExtensionID);
            if (extension == null)
                return this.NotFound();

            extension.Enabled = form.Enabled;
            extension.Name = Text(form.Name);
            extension.Number = Text(form.Number);
            extension.VoicemailAttachRecording = form.VoicemailAttachRecording;
            extension.VoicemailDeleteAfterEmail = form.VoicemailDeleteAfterEmail;
            extension.VoicemailEmail = Text(form.VoicemailEmail);
            extension.VoicemailEnabled = form.VoicemailEnabled;
            extension.VoicemailPin = Text(form.VoicemailPin);

            // Only a new extension carries a password on the form; editing leaves it alone.
            if (isNew)
                extension.Secret = string.IsNullOrWhiteSpace(form.Secret) ? SecretGenerator.Create() : Text(form.Secret);

            try
            {
                if (isNew)
                    this.extensions.Insert(extension);
                else
                    this.extensions.Update(extension);
            }
            catch (ValidationFailedException ex)
            {
                // Hand back what they typed, with the reasons on it.
                form.Errors = ex.Errors.ToList();
                form.Secret = isNew ? extension.Secret : "";
                return this.Partial("_Form", form);
            }

            Log.Info($"Extension {extension.Number} {(isNew ? "created" : "updated")} by {this.User.Identity?.Name}");
            return this.Changed($"Extension {extension.Number} saved.");
        }

        private AmiSettings Ami() => AsteriskSettings.Ami(this.settings.GetAll());

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to.
        /// "extensionsChanged" refreshes the table and the apply banner, "pbxToast" says what
        /// happened.
        /// </summary>
        private IActionResult Changed(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["extensionsChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();
    }
}
