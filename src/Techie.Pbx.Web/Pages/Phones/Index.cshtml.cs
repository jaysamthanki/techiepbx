using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Ami;
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
    /// differently, but an admin manages both the same way — name it, and put an extension on
    /// key 1, which is what the phone registers as (schema 020).
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

            var assigned = this.buttons.GetForPhone(phone.PhoneID);

            return this.Partial("_Form", this.Fill(new PhoneForm
            {
                Brand = phone.Brand,
                Buttons = assigned
                    .Select(button => new PhoneButtonForm { Position = button.Position, Target = button.Key })
                    .ToList(),
                Enabled = phone.Enabled,
                Firmware = phone.Firmware,
                LastConfig = phone.LastConfig,
                LastIP = phone.LastIP,
                LocalSipPort = phone.LocalSipPort,
                Mac = phone.Mac,
                Model = phone.Model,
                Name = phone.Name,
                PhoneID = phone.PhoneID,
                RebootHint = this.RebootHint(phone, assigned),
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
            var lines = this.buttons.GetLines();

            var rows = this.phones.GetAll()
                .Select(phone =>
                {
                    // What a phone registers as is its line key now, not a column on its row
                    // (schema 020), so the table asks the keys.
                    var number = PhoneButton.LineNumber(lines.Where(line => line.PhoneID == phone.PhoneID));
                    var extension = allExtensions.FirstOrDefault(e =>
                        string.Equals(e.Number, number, StringComparison.Ordinal));

                    return new PhoneRow
                    {
                        Brand = phone.Brand,
                        Enabled = phone.Enabled,
                        // The number on its own when the extension has been deleted out from under
                        // the key: "registers as 1001, which is gone" beats "unassigned".
                        Extension = extension != null ? $"{extension.Number} {extension.Name}" : number ?? "",
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
        /// Reboots the phone, for when the daily poll (D79) is too slow to wait for. Confirmed with
        /// sweetalert2 before htmx ever calls this (D42).
        ///
        /// A SIP NOTIFY through Asterisk, addressed to the extension the phone's line key names
        /// (D123). It goes to whatever contact the phone registered from, so unlike the HTTP push
        /// to a Polycom phone's own web UI it reaches a phone behind NAT — which is every phone on
        /// a hosted PBX (D118). Best-effort: a NOTIFY that cannot be sent is named in the toast,
        /// but nothing here is a config change, so there is no undo and no apply.
        /// </summary>
        public IActionResult OnPostReboot(long phoneID)
        {
            var phone = this.phones.GetByID(phoneID);
            if (phone == null)
                return this.NotFound();

            var number = PhoneButton.LineNumber(this.buttons.GetForPhone(phoneID));
            if (number == null)
                return this.Changed($"Phone {phone.Mac} has no line key, so it registers as nothing and there is nowhere to send a reboot.");

            var sent = PhoneNotifier.NotifyReboot(AsteriskSettings.Ami(this.settings.GetAll()), phone.Brand, number);

            Log.Info($"Reboot NOTIFY for phone {phone.Mac} to extension {number} by {this.User.Identity?.Name}: {(sent ? "sent" : "failed")}");

            return this.Changed(sent
                ? $"Reboot sent to {phone.Mac} as a NOTIFY to extension {number}."
                : $"Could not send a reboot to {phone.Mac}. If it is not registered there is nothing to send it to; it will pick up any change at its next poll.");
        }

        /// <summary>
        /// Saves what an admin owns: the keys on the Buttons tab (D121) and the two fields on
        /// Details. Everything else on the row was written by the phone and is left alone, so a
        /// save cannot overwrite what the last provisioning request recorded.
        ///
        /// Assigning the phone to somebody is now one of the keys — key 1, the line it registers
        /// as (schema 020) — so this saves the keys and the rest follows from them.
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

            this.PushConfigReload(phone, assigned);

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
        /// The extensions a key could name, whether as the line this phone registers as or as a
        /// lamp on somebody else's: a disabled extension has no PJSIP endpoint, so it is not
        /// offered — except when a key already names it, which has to stay visible or saving the
        /// form would silently clear that key.
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
                .Where(b => b != null && PhoneButtonTarget.IsExtension(b.TargetType))
                .Select(b => b!.TargetValue)
                .ToList();

            form.Extensions = this.extensions.GetAll()
                .Where(e => e.Enabled || named.Contains(e.Number))
                .ToList();

            form.ParkingSlots = AsteriskSettings.Parking(this.settings.GetAll()).SlotNumbers.ToList();

            return form;
        }

        /// <summary>
        /// Tells the phone to fetch its new config right away rather than waiting for the daily
        /// poll (D79). A Yealink phone gets a SIP NOTIFY through Asterisk, addressed to the
        /// extension its line key names, which asks it to re-read its config without rebooting
        /// (D91). A Polycom phone is pushed over its own web UI instead (D86), because the only
        /// NOTIFY that reaches a Polycom phone reboots it, and rebooting a handset because
        /// somebody renamed it would be worse than waiting.
        ///
        /// Fire-and-forget either way: not awaited for Polycom and the return value ignored for
        /// Yealink, because a phone that cannot be reached still gets there at its next poll, so
        /// nothing here can turn a successful save into a failed one.
        /// </summary>
        private void PushConfigReload(Phone phone, IEnumerable<PhoneButton> assigned)
        {
            if (phone.MatchesBrand(PhoneBrand.Yealink))
            {
                var number = PhoneButton.LineNumber(assigned);

                if (number == null)
                    Log.Info($"Phone {phone.Mac} registers as nothing, so no notify was sent");
                else
                    PhoneNotifier.NotifyCheckConfig(AsteriskSettings.Ami(this.settings.GetAll()), number);

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
        /// Why the Reboot button is disabled, or empty when it is not (D123). A reboot is a NOTIFY
        /// to the contact the phone registered, so it needs a line key to address and a phone that
        /// has actually registered on it. An AMI we could not ask answers Unknown for every
        /// extension, and not knowing is no reason to take the button away — the toast says so if
        /// the send then fails.
        /// </summary>
        private string RebootHint(Phone phone, IEnumerable<PhoneButton> assigned)
        {
            if (!phone.MatchesBrand(PhoneBrand.Polycom))
                return "Rebooting a Yealink phone from here is not built yet.";

            var number = PhoneButton.LineNumber(assigned);
            if (number == null)
                return "This phone registers as nothing, so there is no contact to send a reboot to. Put an extension on key 1.";

            var states = RegistrationStatus.Read(AsteriskSettings.Ami(this.settings.GetAll()), new[] { number });

            return states[number] == RegistrationState.NotRegistered
                ? $"Extension {number} is not registered, so the phone has no contact to send a reboot to."
                : "";
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

            // The keys as they are stored rather than as the failed post left them: what can be
            // rebooted is a question about the phone, not about the edit that did not save.
            form.RebootHint = this.RebootHint(phone, this.buttons.GetForPhone(phone.PhoneID));

            return this.Fill(form);
        }

        /// <summary>A setting's stored value, or null.</summary>
        private static string? Value(IReadOnlyDictionary<string, string> settings, string key) =>
            settings.TryGetValue(key, out var value) ? value : null;
    }
}
