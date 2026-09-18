using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Pages.Phones
{
    /// <summary>
    /// The phones page: the desk phones that have provisioned themselves from us, and which
    /// extension each one registers as (D77, D78). Built like the other list pages — a shell htmx
    /// fills, the form in the shared Bootstrap modal (D42), rows that open their own edit form
    /// (D48).
    ///
    /// Two things it deliberately does not have. There is **no Add button**: a phone appears here
    /// by asking for its config, not by an admin typing a MAC address, because the MAC has to be
    /// right and the phone is the one that knows it. And there is **no apply**: a phone's config is
    /// generated per request, so saving one takes effect at that phone's next poll and nothing is
    /// written to /etc/asterisk (D79) — which is why nothing here raises the config-pending marker.
    /// </summary>
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly ExtensionRepository extensions;
        private readonly PhoneRepository phones;
        private readonly SettingsRepository settings;

        public IndexModel()
        {
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.phones = new PhoneRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        /// <summary>
        /// Whether the provisioning credentials have been set. Without them the endpoint refuses
        /// every phone (D77), so the page says so rather than leaving an admin wondering why no
        /// phone ever turns up.
        /// </summary>
        public bool ProvisioningConfigured { get; private set; }

        public void OnGet()
        {
            var stored = this.settings.GetAll();

            this.ProvisioningConfigured =
                !string.IsNullOrWhiteSpace(Value(stored, SettingsKeys.ProvisioningUsername)) &&
                !string.IsNullOrWhiteSpace(Value(stored, SettingsKeys.ProvisioningPassword));
        }

        /// <summary>The edit form for one phone, which the page shows in the Bootstrap modal.</summary>
        public IActionResult OnGetForm(long phoneID)
        {
            var phone = this.phones.GetByID(phoneID);
            if (phone == null)
                return this.NotFound();

            return this.Partial("_Form", this.Fill(new PhoneForm
            {
                Enabled = phone.Enabled,
                ExtensionID = phone.ExtensionID,
                Firmware = phone.Firmware,
                LastConfig = phone.LastConfig,
                LastIP = phone.LastIP,
                LocalSipPort = phone.LocalSipPort,
                Mac = phone.Mac,
                Model = phone.Model,
                Name = phone.Name,
                PhoneID = phone.PhoneID,
            }));
        }

        /// <summary>The whole table, by MAC address.</summary>
        public PartialViewResult OnGetTable()
        {
            var allExtensions = this.extensions.GetAll();

            var rows = this.phones.GetAll()
                .Select(phone =>
                {
                    var extension = allExtensions.FirstOrDefault(e => e.ExtensionID == phone.ExtensionID);

                    return new PhoneRow
                    {
                        Enabled = phone.Enabled,
                        Extension = extension == null ? "" : $"{extension.Number} {extension.Name}",
                        Firmware = phone.Firmware,
                        LastConfig = phone.LastConfig,
                        LastIP = phone.LastIP,
                        Mac = phone.Mac,
                        Model = phone.Model,
                        Name = phone.Name,
                        PhoneID = phone.PhoneID,
                    };
                })
                .ToList();

            return this.Partial("_Table", rows);
        }

        /// <summary>
        /// Forgets a phone. The hardware is untouched: if it is still plugged in it will ask again
        /// and be auto-added as a new phone, with no extension, which is exactly what should happen
        /// to a handset that has been taken off somebody's desk (D78).
        /// </summary>
        public IActionResult OnPostDelete(long phoneID)
        {
            var phone = this.phones.GetByID(phoneID);
            if (phone == null)
                return this.NotFound();

            this.phones.Delete(phoneID);

            Log.Info($"Phone {phone.Mac} deleted by {this.User.Identity?.Name}");
            return this.Changed($"Phone {phone.Mac} deleted.");
        }

        /// <summary>
        /// Saves the three fields an admin owns. Everything else on the row was written by the
        /// phone and is left alone, so a save cannot overwrite what the last provisioning request
        /// recorded.
        /// </summary>
        public IActionResult OnPostSave(PhoneForm form)
        {
            var phone = this.phones.GetByID(form.PhoneID);
            if (phone == null)
                return this.NotFound();

            phone.Enabled = form.Enabled;
            phone.ExtensionID = form.ExtensionID is > 0 ? form.ExtensionID : null;
            phone.Name = (form.Name ?? "").Trim();

            try
            {
                this.phones.Update(phone);
            }
            catch (ValidationFailedException ex)
            {
                // The read-only half of the form is not posted back, so it is refilled from the
                // row rather than coming back blank under the error message.
                form.Errors = ex.Errors.ToList();
                form.Firmware = phone.Firmware;
                form.LastConfig = phone.LastConfig;
                form.LastIP = phone.LastIP;
                form.LocalSipPort = phone.LocalSipPort;
                form.Mac = phone.Mac;
                form.Model = phone.Model;

                return this.Partial("_Form", this.Fill(form));
            }

            Log.Info($"Phone {phone.Mac} updated by {this.User.Identity?.Name}");
            return this.Changed($"Phone {phone.Mac} saved.");
        }

        /// <summary>A setting's stored value, or null.</summary>
        private static string? Value(IReadOnlyDictionary<string, string> settings, string key) =>
            settings.TryGetValue(key, out var value) ? value : null;

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to.
        /// "phonesChanged" refreshes the table and "pbxToast" says what happened. There is no
        /// "configChanged": a phone changes no generated file, so there is nothing to apply (D79).
        /// </summary>
        private IActionResult Changed(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["phonesChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// The list the form cannot know for itself: the extensions this phone could register as.
        /// A disabled extension has no PJSIP endpoint, so it is not offered — except when it is the
        /// one already chosen, which has to stay visible or saving the form would silently
        /// unassign the phone.
        /// </summary>
        private PhoneForm Fill(PhoneForm form)
        {
            form.Extensions = this.extensions.GetAll()
                .Where(e => e.Enabled || e.ExtensionID == form.ExtensionID)
                .ToList();

            return form;
        }
    }
}
