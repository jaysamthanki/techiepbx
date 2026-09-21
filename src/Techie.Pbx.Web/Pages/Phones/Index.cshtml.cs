using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Asterisk.Provisioning;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Pages.Settings;

namespace Techie.Pbx.Web.Pages.Phones
{
    /// <summary>
    /// The phones page: the desk phones that have provisioned themselves from us, and which
    /// extension each one registers as (D77, D78, D88). Built like the other list pages — a shell
    /// htmx fills, the form in the shared Bootstrap modal (D42), rows that open their own edit
    /// form (D48). Two brands share this one page: Polycom and Yealink provision themselves
    /// differently, but an admin manages both the same way — name it, assign an extension.
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

        private readonly PhoneButtonRepository buttons;
        private readonly ExtensionRepository extensions;
        private readonly PhoneRepository phones;
        private readonly SettingsRepository settings;

        public IndexModel()
        {
            this.buttons = new PhoneButtonRepository(PbxDatabase.Current);
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
                Brand = phone.Brand,
                Buttons = this.buttons.GetForPhone(phone.PhoneID)
                    .Select(button => new PhoneButtonForm { Position = button.Position, Target = button.Key })
                    .ToList(),
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

        /// <summary>
        /// The Polycom device passwords, shown here too so they are discoverable from the page
        /// that actually uses them, on top of the general Settings page they already live on.
        /// </summary>
        public IActionResult OnGetPolycomSettings()
        {
            var stored = this.settings.GetAll();

            var rows = new[] { SettingsKeys.ProvisioningAdminPassword, SettingsKeys.ProvisioningUserPassword }
                .Select(key => SettingRow.For(SettingsCatalog.For(key), stored))
                .ToList();

            return this.Partial("_PolycomSettingsTab", rows);
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
                        Brand = phone.Brand,
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
        /// Pushes a reboot to the phone, for when the daily poll (D79) is too slow to wait for.
        /// Confirmed with sweetalert2 before htmx ever calls this (D84). A Polycom phone is
        /// pushed over its own web UI; a Yealink phone has none, so it gets a SIP NOTIFY through
        /// Asterisk instead, addressed to the extension it is linked to (D91). Best-effort either
        /// way: a phone that cannot be reached is named in the toast, but nothing here is a config
        /// change, so there is no undo and no apply.
        /// </summary>
        public async Task<IActionResult> OnPostReboot(long phoneID)
        {
            var phone = this.phones.GetByID(phoneID);
            if (phone == null)
                return this.NotFound();

            if (phone.MatchesBrand(PhoneBrand.Yealink))
            {
                var sentTo = this.NotifyYealink(phone, reboot: true);

                Log.Info($"Reboot NOTIFY sent for phone {phone.Mac} by {this.User.Identity?.Name}: {(sentTo != null ? "sent" : "skipped, no usable extension")}");

                return this.Changed(sentTo != null
                    ? $"Reboot sent to {phone.Mac} via extension {sentTo}."
                    : $"Phone {phone.Mac} has no extension able to receive a reboot notify.");
            }

            if (phone.LastIP.Length == 0)
                return this.Changed($"Phone {phone.Mac} has never provisioned, so there is no address to reboot it at.");

            var stored = this.settings.GetAll();
            if (!stored.TryGetValue(SettingsKeys.ProvisioningAdminPassword, out var adminPassword) || adminPassword.Length == 0)
                return this.Changed("Set Provisioning.AdminPassword on the Settings page before rebooting a phone.");

            var sent = await PolycomPusher.PushReboot(phone.LastIP, adminPassword);

            Log.Info($"Reboot pushed to phone {phone.Mac} at {phone.LastIP} by {this.User.Identity?.Name}: {(sent ? "accepted" : "not confirmed")}");

            return this.Changed(sent
                ? $"Reboot sent to {phone.Mac}."
                : $"Could not reach {phone.Mac} at {phone.LastIP}; it will pick up any change at its next poll.");
        }

        /// <summary>
        /// Saves what an admin owns: the keys on the Buttons tab (D121) and the three fields on
        /// Details. Everything else on the row was written by the phone and is left alone, so a
        /// save cannot overwrite what the last provisioning request recorded.
        ///
        /// A key left on "Nothing" posts an empty target and is simply not among the rows saved,
        /// which is how a key is cleared: the whole set is replaced every time.
        /// </summary>
        public IActionResult OnPostSave(PhoneForm form)
        {
            var phone = this.phones.GetByID(form.PhoneID);
            if (phone == null)
                return this.NotFound();

            phone.Enabled = form.Enabled;
            phone.ExtensionID = form.ExtensionID is > 0 ? form.ExtensionID : null;
            phone.Name = (form.Name ?? "").Trim();

            var assigned = new List<PhoneButton>();
            var unreadable = new List<string>();

            foreach (var row in form.Buttons.Where(b => !string.IsNullOrWhiteSpace(b.Target)))
            {
                if (PhoneButton.TryParse(row.Target, row.Position, out var button))
                    assigned.Add(button);
                else
                    unreadable.Add($"Key {row.Position}: that is not something a key can be put on.");
            }

            if (unreadable.Count > 0)
                return this.Partial("_Form", this.Refill(form, phone, unreadable));

            try
            {
                this.phones.Update(phone);
                this.buttons.Replace(phone.PhoneID, assigned);
            }
            catch (ValidationFailedException ex)
            {
                return this.Partial("_Form", this.Refill(form, phone, ex.Errors));
            }

            this.PushConfigReload(phone);

            Log.Info($"Phone {phone.Mac} updated by {this.User.Identity?.Name}");
            return this.Changed($"Phone {phone.Mac} saved.");
        }

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
        /// The lists the form cannot know for itself.
        ///
        /// The extensions this phone could register as, or put a key on: a disabled extension has
        /// no PJSIP endpoint, so it is not offered — except when it is the one already chosen or
        /// one a key already names, which have to stay visible or saving the form would silently
        /// unassign the phone or clear the key.
        ///
        /// The parking slots, which exist only when parking is switched on (D119), and one row per
        /// assignable key whatever arrived: a posted form carries them all, a GET carries only the
        /// keys that have something on them, and either way the tab shows every key (D121).
        /// </summary>
        private PhoneForm Fill(PhoneForm form)
        {
            var posted = form.Buttons
                .Where(b => b.Position >= PhoneButton.FirstPosition && b.Position <= PhoneButton.Count)
                .GroupBy(b => b.Position)
                .ToDictionary(g => g.Key, g => g.First().Target);

            form.Buttons = Enumerable.Range(PhoneButton.FirstPosition, PhoneButton.Count)
                .Select(position => new PhoneButtonForm
                {
                    Position = position,
                    Target = posted.TryGetValue(position, out var target) ? target : "",
                })
                .ToList();

            var named = form.Buttons
                .Select(b => PhoneButton.TryParse(b.Target, b.Position, out var button) ? button : null)
                .Where(b => b is { TargetType: PhoneButtonTarget.Extension })
                .Select(b => b!.TargetValue)
                .ToList();

            form.Extensions = this.extensions.GetAll()
                .Where(e => e.Enabled || e.ExtensionID == form.ExtensionID || named.Contains(e.Number))
                .ToList();

            form.ParkingSlots = AsteriskSettings.Parking(this.settings.GetAll()).SlotNumbers.ToList();

            return form;
        }

        /// <summary>
        /// The Yealink half of a push: a SIP NOTIFY sent through Asterisk over AMI, because a
        /// Yealink phone has no web endpoint to push to the way a Polycom one does (D91). Needs
        /// the extension the phone is linked to — the NOTIFY is addressed to its registered
        /// contacts, so a phone with nobody assigned has nowhere to send it.
        /// </summary>
        private string? NotifyYealink(Phone phone, bool reboot)
        {
            var extension = phone.ExtensionID is > 0 ? this.extensions.GetByID(phone.ExtensionID.Value) : null;
            if (extension is not { Enabled: true })
            {
                Log.Info($"Phone {phone.Mac} has no usable extension, so no notify was sent");
                return null;
            }

            var ami = AsteriskSettings.Ami(this.settings.GetAll());
            var sent = reboot
                ? YealinkNotifier.NotifyReboot(ami, extension.Number)
                : YealinkNotifier.NotifyCheckConfig(ami, extension.Number);

            return sent ? extension.Number : null;
        }

        /// <summary>
        /// Tells the phone to fetch its new config right away rather than waiting for the daily
        /// poll (D79). A Polycom phone is pushed over its own web UI (D84); a Yealink phone gets
        /// the same SIP NOTIFY <see cref="NotifyYealink"/> sends for a reboot, with a different
        /// category (D91). Fire-and-forget either way: not awaited for Polycom and the return
        /// value ignored for Yealink, because a phone that cannot be reached still gets there at
        /// its next poll, so nothing here can turn a successful save into a failed one.
        /// </summary>
        private void PushConfigReload(Phone phone)
        {
            if (phone.MatchesBrand(PhoneBrand.Yealink))
            {
                this.NotifyYealink(phone, reboot: false);
                return;
            }

            if (phone.LastIP.Length == 0)
                return;

            var stored = this.settings.GetAll();
            if (!stored.TryGetValue(SettingsKeys.ProvisioningAdminPassword, out var adminPassword) || adminPassword.Length == 0)
                return;

            _ = PolycomPusher.PushUpdateConfig(phone.LastIP, adminPassword);
        }

        /// <summary>
        /// The form as it comes back under an error message. The read-only half of it is not
        /// posted, so it is refilled from the row rather than coming back blank.
        /// </summary>
        private PhoneForm Refill(PhoneForm form, Phone phone, IEnumerable<string> errors)
        {
            form.Errors = errors.ToList();
            form.Brand = phone.Brand;
            form.Firmware = phone.Firmware;
            form.LastConfig = phone.LastConfig;
            form.LastIP = phone.LastIP;
            form.LocalSipPort = phone.LocalSipPort;
            form.Mac = phone.Mac;
            form.Model = phone.Model;

            return this.Fill(form);
        }

        /// <summary>A setting's stored value, or null.</summary>
        private static string? Value(IReadOnlyDictionary<string, string> settings, string key) =>
            settings.TryGetValue(key, out var value) ? value : null;
    }
}
