using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Settings
{
    /// <summary>
    /// The settings page: one row per key in <see cref="SettingsKeys"/>, whether or not anything
    /// has been stored for it (D67). Built like the other list pages — a shell htmx fills, the
    /// form in the shared Bootstrap modal (D42), rows that open their own edit form (D48).
    ///
    /// Two things are its own. The table masks a secret with dots, but the edit form shows the
    /// stored value in the clear (D112). And blank means "put it back to the default" rather
    /// than "store an empty string", so the table always shows either a value somebody chose
    /// or the default the code will use.
    /// </summary>
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly SettingsRepository settings;

        public IndexModel()
        {
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>The edit form for one setting, which the page shows in the Bootstrap modal.</summary>
        public IActionResult OnGetForm(string? key)
        {
            key = Text(key);
            if (!SettingsKeys.IsKnown(key))
                return this.NotFound();

            var stored = this.settings.Get(key);

            return this.Partial("_Form", new SettingForm
            {
                Descriptor = SettingsCatalog.For(key),
                IsSet = stored != null,
                Key = key,

                // The stored value is in the form, secret or not (D112): the table keeps its
                // dots, the edit screen shows what is actually there.
                Value = stored ?? "",
            });
        }

        /// <summary>The whole table: every known key, stored or not.</summary>
        public PartialViewResult OnGetTable()
        {
            var stored = this.settings.GetAll();
            var rows = SettingsCatalog.All.Select(descriptor => SettingRow.For(descriptor, stored)).ToList();

            return this.Partial("_Table", rows);
        }

        /// <summary>
        /// Puts one setting back to its built-in default by removing the row. The confirm is
        /// sweetalert2's, asked by hx-confirm before this is ever called.
        /// </summary>
        public IActionResult OnPostReset(string? key)
        {
            var name = Text(key);
            if (!SettingsKeys.IsKnown(name))
                return this.NotFound();

            this.settings.Delete(name);

            Log.Info($"Setting {name} reset to its default by {this.User.Identity?.Name}");
            return this.Changed(name, $"{name} is back to its default.");
        }

        /// <summary>
        /// Stores one setting. Validation failures come back as the form again, with the reasons
        /// on it, which htmx swaps into the open modal.
        ///
        /// Blank clears the setting rather than storing an empty string — the stored value is in
        /// the box to start with (D112), so clearing it is a deliberate act.
        /// </summary>
        public IActionResult OnPostSave(SettingForm form)
        {
            var key = Text(form.Key);
            if (!SettingsKeys.IsKnown(key))
                return this.NotFound();

            var value = Text(form.Value);

            form.Descriptor = SettingsCatalog.For(key);
            form.IsSet = this.settings.Get(key) != null;
            form.Key = key;
            form.Value = value;

            try
            {
                if (value.Length == 0)
                    this.settings.Delete(key);
                else
                    this.settings.Set(key, value);
            }
            catch (ValidationFailedException ex)
            {
                form.Errors = ex.Errors.ToList();
                return this.Partial("_Form", form);
            }

            Log.Info($"Setting {key} changed by {this.User.Identity?.Name}");
            return this.Changed(key, value.Length == 0 ? $"{key} is back to its default." : $"{key} saved.");
        }

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to.
        /// "settingsChanged" refreshes the table and "pbxToast" says what happened.
        ///
        /// "configChanged", which wakes the navbar's apply button (D43), depends on the key: only
        /// an Asterisk-scoped setting is written into a generated conf file, so only that one has
        /// anything to apply (D103). A phone setting is already live — those files are generated
        /// per request (D79) — and the toast says so instead, and an app setting changes nothing
        /// outside this process.
        /// </summary>
        private IActionResult Changed(string key, string message)
        {
            var scope = SettingsKeys.ScopeOf(key);

            var events = new Dictionary<string, object?>
            {
                ["settingsChanged"] = null,
                ["pbxToast"] = new { message = message + ScopeNote(scope) },
            };

            if (scope == SettingScope.Asterisk)
                events["configChanged"] = null;

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// What the toast adds about where the change has got to. Nothing for an Asterisk-scoped
        /// key: the apply button turning red says it better than a sentence would.
        /// </summary>
        private static string ScopeNote(SettingScope scope) => scope switch
        {
            SettingScope.Phones => " Phone configs are generated per request, so each phone picks this up at its next poll.",
            _ => "",
        };

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();
    }
}
