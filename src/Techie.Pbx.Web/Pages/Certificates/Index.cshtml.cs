using System.Globalization;
using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Certificates;

namespace Techie.Pbx.Web.Pages.Certificates
{
    /// <summary>
    /// The certificates page, built like every other list page: a shell htmx fills from the
    /// handlers here, the shared Bootstrap modal for the form (D42), rows that open their own edit
    /// form (D48), and changes answered with 204 and HX-Trigger events (D21).
    ///
    /// The one thing that is not like the others: saving a new certificate <em>orders</em> it, and
    /// an ACME order is a conversation with a server that can take a minute. There is no
    /// background job to hide behind — an admin who pressed "Order" is waiting for the answer, and
    /// the answer is either a certificate or a message saying why not (D97).
    /// </summary>
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly CertificateRepository certificates;
        private readonly SettingsRepository settings;

        public IndexModel()
        {
            this.certificates = new CertificateRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        /// <summary>Whether a contact address is stored, without which no order can be made.</summary>
        public bool ContactConfigured { get; private set; }

        /// <summary>What the running web server is serving, for the banner at the top.</summary>
        public string ServingNow { get; private set; } = "";

        public void OnGet()
        {
            this.ContactConfigured = !string.IsNullOrWhiteSpace(this.settings.Get(SettingsKeys.CertEmail));

            var loaded = KestrelCertificate.LoadedID == 0
                ? null
                : this.certificates.GetByID(KestrelCertificate.LoadedID);

            this.ServingNow = loaded == null
                ? ""
                : $"{loaded.Name} ({loaded.Hostnames})";
        }

        public IActionResult OnPostDelete(long certificateID)
        {
            var certificate = this.certificates.GetByID(certificateID);
            if (certificate == null)
                return this.NotFound();

            this.certificates.Delete(certificateID);

            Log.Info($"Certificate {certificate.Name} deleted by {this.User.Identity?.Name}");
            return this.Changed($"Certificate {certificate.Name} deleted. Apply config to take it off Asterisk, and restart the app to stop serving it.");
        }

        /// <summary>The order or edit form, shown in the shared Bootstrap modal.</summary>
        public IActionResult OnGetForm(long? certificateID)
        {
            if (certificateID is null or 0)
                return this.Partial("_Form", new CertificateForm());

            var certificate = this.certificates.GetByID(certificateID.Value);
            if (certificate == null)
                return this.NotFound();

            return this.Partial("_Form", new CertificateForm
            {
                CertificateID = certificate.CertificateID,
                Enabled = certificate.Enabled,
                Errors = certificate.LastError == null ? new List<string>() : new List<string> { certificate.LastError },
                Hostnames = string.Join("\n", certificate.HostnameList()),
                IsIssued = certificate.HasKeyPair,
                Name = certificate.Name,
            });
        }

        public PartialViewResult OnGetTable()
        {
            var now = DateTimeOffset.UtcNow;
            var rows = this.certificates.GetAll().Select(certificate => Row(certificate, now)).ToList();

            return this.Partial("_Table", rows);
        }

        /// <summary>
        /// Orders this certificate again. The same ACME conversation as the first order (D100), so
        /// it is also the "try again" button for one that failed.
        /// </summary>
        public async Task<IActionResult> OnPostRenew(long certificateID)
        {
            var certificate = this.certificates.GetByID(certificateID);
            if (certificate == null)
                return this.NotFound();

            Log.Info($"Certificate {certificate.Name} renewal requested by {this.User.Identity?.Name}");

            var result = await new AcmeCertificateService(PbxDatabase.Current).Order(certificate);

            if (result.LastError != null)
                return this.Failed(result);

            return this.Changed(Issued(result));
        }

        /// <summary>
        /// Creates or updates one certificate. A new one is ordered as soon as it is stored,
        /// because a certificate row with nothing in it is not something anybody wants to keep;
        /// editing an existing one only changes the row, and Renew is what orders again.
        /// </summary>
        public async Task<IActionResult> OnPostSave(CertificateForm form)
        {
            var isNew = form.CertificateID == 0;
            var certificate = isNew ? new Certificate() : this.certificates.GetByID(form.CertificateID);
            if (certificate == null)
                return this.NotFound();

            certificate.Enabled = form.Enabled;
            certificate.Hostnames = Text(form.Hostnames);
            certificate.Name = Text(form.Name);

            try
            {
                if (isNew)
                    this.certificates.Insert(certificate);
                else
                    this.certificates.Update(certificate);
            }
            catch (ValidationFailedException ex)
            {
                form.Errors = ex.Errors.ToList();
                return this.Partial("_Form", form);
            }

            if (!isNew)
            {
                Log.Info($"Certificate {certificate.Name} updated by {this.User.Identity?.Name}");
                return this.Changed($"Certificate {certificate.Name} saved.");
            }

            Log.Info($"Certificate {certificate.Name} created by {this.User.Identity?.Name}; ordering");

            var result = await new AcmeCertificateService(PbxDatabase.Current).Order(certificate);

            if (result.LastError != null)
            {
                // The row is stored either way, so the admin can fix DNS and press Renew rather
                // than typing the whole thing in again.
                form.CertificateID = certificate.CertificateID;
                form.Errors = new List<string> { result.LastError };
                return this.Partial("_Form", form);
            }

            return this.Changed(Issued(result));
        }

        /// <summary>What to say when an order worked, including the part that needs a restart.</summary>
        private static string Issued(Certificate certificate) =>
            $"Certificate {certificate.Name} issued, expires {Date(certificate.Expires)}. " +
            "Apply config for SIP TLS, and restart the app for the web server to serve it.";

        /// <summary>A date as the table and the toasts show it.</summary>
        private static string Date(DateTimeOffset? value) =>
            value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";

        /// <summary>One table row: the status in words and the colour that goes with it.</summary>
        private static CertificateRow Row(Certificate certificate, DateTimeOffset now)
        {
            var days = certificate.DaysUntilExpiry(now);

            var row = new CertificateRow
            {
                CertificateID = certificate.CertificateID,
                DaysLeft = days?.ToString(CultureInfo.InvariantCulture) ?? "—",
                Expires = Date(certificate.Expires),
                Hostnames = string.Join(", ", certificate.HostnameList()),
                LastError = certificate.LastError ?? "",
                Name = certificate.Name,
            };

            if (!certificate.Enabled)
            {
                row.BadgeClass = "bg-secondary";
                row.Status = "Disabled";
            }
            else if (!certificate.HasKeyPair)
            {
                row.BadgeClass = certificate.LastError == null ? "bg-secondary" : "bg-danger";
                row.Status = certificate.LastError == null ? "Not issued" : "Order failed";
            }
            else if (!certificate.IsUsable(now))
            {
                row.BadgeClass = "bg-danger";
                row.Status = "Expired";
            }
            else
            {
                row.BadgeClass = days <= Certificate.RenewalThresholdDays ? "bg-warning text-dark" : "bg-success";
                row.Status = "Active";
            }

            // A renewed certificate is on disk and in the database before the web server has it
            // (D99). Saying so per row is what stops "I renewed it and nothing changed".
            if (certificate.IsUsable(now) && !KestrelCertificate.IsLoaded(certificate.CertificateID, certificate.ExpiresUtc))
                row.Note = "Restart the app to serve this in the browser";

            return row;
        }

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to.
        /// "certificatesChanged" refreshes the table, "configChanged" lights the apply button
        /// because the TLS transport is generated from these rows (D101).
        /// </summary>
        private IActionResult Changed(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["certificatesChanged"] = null,
                ["configChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// An order that did not work, from a button rather than from the form: the table still
        /// has to refresh, because the row now carries the error it should show.
        /// </summary>
        private IActionResult Failed(Certificate certificate)
        {
            var events = new Dictionary<string, object?>
            {
                ["certificatesChanged"] = null,
                ["pbxAlert"] = new { message = $"Certificate {certificate.Name}: {certificate.LastError}" },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }
    }
}
