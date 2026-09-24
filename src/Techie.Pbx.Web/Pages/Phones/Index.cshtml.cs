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
using Techie.Pbx.Web.Controllers;
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
    ///
    /// It also carries the one site-wide Polycom background image (D145, D151) and logo (D153): a
    /// status line each near the top and a modal to upload or remove each. Not per-phone, and like
    /// everything else here they change no generated file — every phone's config names the images
    /// from its next fetch.
    /// </summary>
    [RequestSizeLimit(BackgroundStore.MaxUploadBytes + (1024 * 1024))]
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly BackgroundStore background;
        private readonly PhoneButtonRepository buttons;
        private readonly CallFlowControlRepository callFlowControls;
        private readonly ExtensionRepository extensions;
        private readonly LogoStore logo;
        private readonly PhoneRepository phones;
        private readonly SettingsRepository settings;

        public IndexModel()
        {
            this.background = new BackgroundStore(PbxDatabase.Current);
            this.buttons = new PhoneButtonRepository(PbxDatabase.Current);
            this.callFlowControls = new CallFlowControlRepository(PbxDatabase.Current);
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.logo = new LogoStore(PbxDatabase.Current);
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

        /// <summary>The background image's status line near the top of the page (D151).</summary>
        public PartialViewResult OnGetBackground()
        {
            return this.Partial("_Background", this.FillBackground(new BackgroundForm()));
        }

        /// <summary>The upload/remove form for the background image, which the page shows in the modal.</summary>
        public PartialViewResult OnGetBackgroundForm()
        {
            return this.Partial("_BackgroundForm", this.FillBackground(new BackgroundForm()));
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

        /// <summary>The logo's status line, under the background's (D153).</summary>
        public PartialViewResult OnGetLogo()
        {
            return this.Partial("_Logo", this.FillLogo(new LogoForm()));
        }

        /// <summary>The upload/remove form for the logo, which the page shows in the modal.</summary>
        public PartialViewResult OnGetLogoForm()
        {
            return this.Partial("_LogoForm", this.FillLogo(new LogoForm()));
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
        /// Removes the site's background image. Nothing else changes: the next config any Polycom
        /// fetches has no <c>bg</c> element, and the phone goes back to its own background (D145).
        /// </summary>
        public IActionResult OnPostRemoveBackground()
        {
            bool removed;

            try
            {
                removed = this.background.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"The Polycom background image could not be removed: {ex.Message}", ex);
                return this.Partial("_BackgroundForm", this.FillBackground(new BackgroundForm
                {
                    Errors = new List<string> { "The image could not be removed from the data folder. Check that the web user may write to it." },
                }));
            }

            Log.Info($"Polycom background image {(removed ? "removed" : "remove requested, but none was set")} by {this.User.Identity?.Name}");
            return this.ImageChanged("phoneBackgroundChanged", removed
                ? "Background image removed. Polycom phones go back to their own at their next config fetch."
                : "There was no background image to remove.");
        }

        /// <summary>
        /// Removes the site's logo. Nothing else changes: the next config any Polycom fetches has
        /// no <c>bg.logo</c> line, and the phone goes back to Poly's own logo (D153).
        /// </summary>
        public IActionResult OnPostRemoveLogo()
        {
            bool removed;

            try
            {
                removed = this.logo.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"The Polycom logo could not be removed: {ex.Message}", ex);
                return this.Partial("_LogoForm", this.FillLogo(new LogoForm
                {
                    Errors = new List<string> { "The logo could not be removed from the data folder. Check that the web user may write to it." },
                }));
            }

            Log.Info($"Polycom logo {(removed ? "removed" : "remove requested, but none was set")} by {this.User.Identity?.Name}");
            return this.ImageChanged("phoneLogoChanged", removed
                ? "Logo removed. Polycom phones go back to Poly's own at their next config fetch."
                : "There was no logo to remove.");
        }

        /// <summary>
        /// The configuration the phone would be served if it asked for it right now: what the
        /// View config button in the edit modal opens (D121). Read-only — it builds exactly what
        /// the provisioning controller would build from the same rows, without updating anything
        /// the phone owns, so looking cannot change what a later real request records. The one
        /// difference from the phone's own request: a preview needs the phone to already exist,
        /// which on this page it always does.
        /// </summary>
        public IActionResult OnGetConfig(long phoneID)
        {
            var phone = this.phones.GetByID(phoneID);
            if (phone == null)
                return this.NotFound();

            var stored = this.settings.GetAll();
            var transport = AsteriskSettings.Transport(stored);
            var allExtensions = this.extensions.GetAll();
            var controls = this.callFlowControls.GetAll();
            var usable = PhoneButton.Usable(
                this.buttons.GetForPhone(phoneID), allExtensions, AsteriskSettings.Parking(stored).SlotNumbers, controls);

            // The config assembly is shared with the provisioning controllers
            // (PhoneConfigFactory), so what an admin previews here is exactly what a phone
            // would be served — they cannot drift (D121).
            if (phone.MatchesBrand(PhoneBrand.Polycom))
            {
                var polycom = PhoneConfigFactory.Polycom(
                    phone, usable, allExtensions, controls, stored, transport,
                    this.Request.Scheme, this.Request.Host.Host, this.background.Current(), this.logo.Current());

                return this.Content(PolycomConfigRenderer.Render(polycom), "text/plain");
            }

            var yealink = PhoneConfigFactory.Yealink(
                phone, usable, allExtensions, controls, stored, transport, this.Request.Scheme, this.Request.Host.Host);

            return this.Content(YealinkConfigRenderer.Render(yealink), "text/plain");
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
        /// Stores an uploaded background image, replacing whatever was there (D151, D152), resized
        /// to 320x240 unless it is that size already (D154). The contents decide whether it is a
        /// PNG or a JPEG, never the file name, and anything refused comes back as a message on the
        /// form with the current image untouched.
        /// </summary>
        public IActionResult OnPostSaveBackground(BackgroundForm form)
        {
            if (form.Image is not { Length: > 0 })
            {
                form.Errors = new List<string> { "Choose an image to upload." };
                return this.Partial("_BackgroundForm", this.FillBackground(form));
            }

            BackgroundImage image;

            try
            {
                using var upload = form.Image.OpenReadStream();
                image = this.background.Save(upload);
            }
            catch (BackgroundUploadException ex)
            {
                Log.Warn($"Polycom background upload refused: {ex.Message}");
                form.Errors = new List<string> { ex.Message };
                return this.Partial("_BackgroundForm", this.FillBackground(form));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"The Polycom background image could not be written: {ex.Message}", ex);
                form.Errors = new List<string> { "The image could not be written to the data folder. Check that the web user may write to it." };
                return this.Partial("_BackgroundForm", this.FillBackground(form));
            }

            Log.Info($"Polycom background image uploaded ({BackgroundImageSignature.Describe(image.Format)}, {image.Bytes} bytes) by {this.User.Identity?.Name}");
            return this.ImageChanged("phoneBackgroundChanged", "Background image saved. Polycom phones pick it up at their next config fetch, or straight away if you reboot one.");
        }

        /// <summary>
        /// Stores an uploaded logo, replacing whatever was there (D153), resized to fit inside 60x26
        /// on transparency unless it is that size already (D154). Otherwise exactly the background's
        /// save: the contents decide PNG or JPEG, and anything refused comes back as a message on
        /// the form with the current logo untouched.
        /// </summary>
        public IActionResult OnPostSaveLogo(LogoForm form)
        {
            if (form.Image is not { Length: > 0 })
            {
                form.Errors = new List<string> { "Choose an image to upload." };
                return this.Partial("_LogoForm", this.FillLogo(form));
            }

            BackgroundImage image;

            try
            {
                using var upload = form.Image.OpenReadStream();
                image = this.logo.Save(upload);
            }
            catch (BackgroundUploadException ex)
            {
                Log.Warn($"Polycom logo upload refused: {ex.Message}");
                form.Errors = new List<string> { ex.Message };
                return this.Partial("_LogoForm", this.FillLogo(form));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"The Polycom logo could not be written: {ex.Message}", ex);
                form.Errors = new List<string> { "The logo could not be written to the data folder. Check that the web user may write to it." };
                return this.Partial("_LogoForm", this.FillLogo(form));
            }

            Log.Info($"Polycom logo uploaded ({BackgroundImageSignature.Describe(image.Format)}, {image.Bytes} bytes) by {this.User.Identity?.Name}");
            return this.ImageChanged("phoneLogoChanged", "Logo saved. Polycom phones pick it up at their next config fetch, or straight away if you reboot one.");
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
            form.CallFlowControls = this.callFlowControls.GetAll()
                .OrderBy(c => c.FeatureCode.Length)
                .ThenBy(c => c.FeatureCode, StringComparer.Ordinal)
                .ToList();

            return form;
        }

        /// <summary>What the background form and status line cannot know for themselves: what is on disk now.</summary>
        private BackgroundForm FillBackground(BackgroundForm form)
        {
            var image = this.background.Current();

            form.HasImage = image != null;
            form.Summary = Summary(image, "None — Polycom phones show their own background.");

            return form;
        }

        /// <summary>What the logo form and status line cannot know for themselves: what is on disk now.</summary>
        private LogoForm FillLogo(LogoForm form)
        {
            var image = this.logo.Current();

            form.HasImage = image != null;
            form.Summary = Summary(image, "None — Polycom phones show Poly's own logo.");

            return form;
        }

        /// <summary>
        /// The answer to a background or logo change: no content to swap, that image's status line
        /// refreshed by <paramref name="refreshEvent"/>, and a toast. No "phonesChanged" — no phone
        /// row changed — and no "configChanged": the images are named in configs generated per
        /// request, so there is nothing to apply (D79).
        /// </summary>
        private IActionResult ImageChanged(string refreshEvent, string message)
        {
            var events = new Dictionary<string, object?>
            {
                [refreshEvent] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
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
        /// has actually registered on it. The same for both brands: only the NOTIFY differs, and
        /// <see cref="PhoneNotifier.NotifyReboot"/> picks it (D134). An AMI we could not ask answers Unknown for every
        /// extension, and not knowing is no reason to take the button away — the toast says so if
        /// the send then fails.
        /// </summary>
        private string RebootHint(Phone phone, IEnumerable<PhoneButton> assigned)
        {
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

        /// <summary>How a stored image reads on its status line, e.g. "PNG, 142 KB", or <paramref name="none"/>.</summary>
        private static string Summary(BackgroundImage? image, string none) =>
            image == null
                ? none
                : $"{BackgroundImageSignature.Describe(image.Format)}, {Math.Max(1, image.Bytes / 1024)} KB";

        /// <summary>A setting's stored value, or null.</summary>
        private static string? Value(IReadOnlyDictionary<string, string> settings, string key) =>
            settings.TryGetValue(key, out var value) ? value : null;
    }
}
