using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Asterisk.Migration;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Migration;
using Techie.Pbx.Web.Controllers;

namespace Techie.Pbx.Web.Pages.Migration
{
    /// <summary>
    /// Import from FreePBX (D170): upload the exporter's tarball, read exactly what would land,
    /// confirm, read what did. Three screens on one page, in the order they happen:
    ///
    /// <list type="number">
    /// <item><b>Upload</b>, in the shared modal like every other form here (D42). The tarball is
    /// unpacked into a private staging directory and its manifest read; nothing touches the
    /// database.</item>
    /// <item><b>Preview</b>, a plain GET of the staged upload: the plan, every warning, and the
    /// import button — hollow and disabled until the operator says they have read it.</item>
    /// <item><b>Report</b>, what the import wrote and every warning, after it has also applied the
    /// config the way the navbar's button does.</item>
    /// </list>
    ///
    /// The plan is worked out again when the import is confirmed rather than carried between
    /// requests, so what lands is what the database says at that moment and nothing the browser
    /// sent can change it but the one checkbox the preview offers.
    /// </summary>
    [RequestSizeLimit(MigrationArchive.MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MigrationArchive.MaxUploadBytes)]
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly MigrationStaging staging;

        /// <summary>Something to say at the top of the page: an upload that has gone, a plan that could not be read.</summary>
        public string Error { get; set; } = "";

        /// <summary>The staged upload being previewed, which the import form posts back.</summary>
        public string ID { get; set; } = "";

        public ImportOptions Options { get; set; } = new();

        /// <summary>The preview, when there is a staged upload to show.</summary>
        public ImportPlan? Plan { get; set; }

        /// <summary>The report, once an import has run.</summary>
        public ImportReport? Report { get; set; }

        public IndexModel()
        {
            this.staging = MigrationStaging.Beside(PbxDatabase.Current);
        }

        /// <summary>The page, and the preview of an upload when its ID is in the URL.</summary>
        public void OnGet(string? id)
        {
            if (id == null)
                return;

            this.Plan = this.Load(id);
        }

        /// <summary>The upload form, which the page shows in the Bootstrap modal.</summary>
        public PartialViewResult OnGetForm() => this.Partial("_UploadForm", new UploadForm());

        /// <summary>Throws the staged upload away without importing it.</summary>
        public IActionResult OnPostDiscard(string? id)
        {
            if (id != null)
                this.staging.Delete(id);

            Log.Info($"Staged FreePBX import discarded by {this.User.Identity?.Name}");
            return this.RedirectToPage("Index");
        }

        /// <summary>
        /// The import itself. The plan is made again from the staged manifest and today's
        /// database, written row by row, and then applied. The staged upload is deleted
        /// afterwards — all of it, unless voicemail is left to copy by hand.
        /// </summary>
        public IActionResult OnPostImport(string? id, bool userEmailFromVoicemail, bool reviewed)
        {
            this.Options.UserEmailFromVoicemail = userEmailFromVoicemail;

            var plan = id == null ? null : this.Load(id);
            if (plan == null)
                return this.Page();

            if (!reviewed)
            {
                this.Plan = plan;
                this.Error = "Tick the box to say you have read the preview and its warnings, then import.";
                return this.Page();
            }

            Log.Info($"FreePBX import of {id} confirmed by {this.User.Identity?.Name}");

            var report = new MigrationImporter(PbxDatabase.Current, PbxSounds.Current, VoicemailSpool.Root).Run(plan);
            this.Apply(report);

            try
            {
                if (report.StagingKept)
                    this.staging.KeepVoicemailOnly(id!);
                else
                    this.staging.Delete(id!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"The staged import {id} could not be cleaned up: {ex.Message}");
            }

            this.Report = report;
            return this.Page();
        }

        /// <summary>
        /// Takes the tarball, unpacks it into a fresh staging directory and reads its manifest.
        /// Anything wrong comes back on the form with the staging directory gone; a good upload
        /// sends the browser to its preview.
        /// </summary>
        public IActionResult OnPostUpload(IFormFile? archive)
        {
            var form = new UploadForm();

            if (archive == null || archive.Length == 0)
            {
                form.Errors.Add("Choose the tnpbx-migrate.tar.gz the exporter wrote.");
                return this.Partial("_UploadForm", form);
            }

            var id = this.staging.Create();
            var path = this.staging.PathFor(id)!;

            try
            {
                using (var stream = archive.OpenReadStream())
                    MigrationArchive.Extract(stream, path);

                MigrationManifest.Load(Path.Combine(path, "manifest.json"));
            }
            catch (MigrationArchiveException ex)
            {
                Log.Warn($"FreePBX export refused for {this.User.Identity?.Name}: {ex.Message}");
                this.staging.Delete(id);
                form.Errors.Add(ex.Message);
                return this.Partial("_UploadForm", form);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"FreePBX export could not be unpacked into {path}: {ex.Message}", ex);
                this.staging.Delete(id);
                form.Errors.Add($"The export could not be unpacked into {this.staging.Root}: {ex.Message}");
                return this.Partial("_UploadForm", form);
            }

            Log.Info($"FreePBX export uploaded by {this.User.Identity?.Name} ({archive.Length} bytes), staged as {id}");

            this.Response.Headers["HX-Redirect"] = this.Url.Page("Index", new { id });
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// The same apply the navbar's button asks /api/config/apply for (D33, D104): render
        /// everything, reload what changed, and raise the restart marker when a startup-only file
        /// was written. Failures are the report's to show, worded the way the button words them.
        /// </summary>
        private void Apply(ImportReport report)
        {
            var database = PbxDatabase.Current;
            var applier = ConfigApplier.FromDatabase(
                database, new SettingsRepository(database), new ExtensionRepository(database), new TrunkRepository(database),
                new OutboundRouteRepository(database), new InboundRouteRepository(database), new RingGroupRepository(database),
                new AnnouncementRepository(database), new IvrRepository(database), new TimeConditionRepository(database),
                new MohFileRepository(database));

            try
            {
                var result = applier.Apply();

                if (result.RestartRequired)
                    new AsteriskRestartMarker(database).Raise();

                report.ApplySummary = ConfigController.Summarise(result);
            }
            catch (AmiException ex)
            {
                Log.Error($"Import apply: the files were written but Asterisk could not be reloaded: {ex.Message}", ex);
                report.ApplyFailed = true;
                report.ApplySummary = "The config files were written, but Asterisk could not be reloaded over AMI. " +
                                      "Check that Asterisk is running, then use Apply config in the toolbar.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Import apply: could not write the config files: {ex.Message}", ex);
                report.ApplyFailed = true;
                report.ApplySummary = "The config files could not be written. Check that the conf directory exists and that the web user may write to it.";
            }
            catch (InvalidOperationException ex)
            {
                Log.Error($"Import apply: the config could not be rendered: {ex.Message}", ex);
                report.ApplyFailed = true;
                report.ApplySummary = "The config could not be generated: " + ex.Message;
            }
        }

        /// <summary>
        /// The plan for a staged upload, or null with <see cref="Error"/> saying why: gone, or a
        /// manifest that no longer reads.
        /// </summary>
        private ImportPlan? Load(string id)
        {
            var path = this.staging.PathFor(id);

            if (path == null)
            {
                this.Error = "That upload is no longer on the server — it was imported, discarded, or replaced by a newer one. Upload the export again.";
                return null;
            }

            try
            {
                var manifest = MigrationManifest.Load(Path.Combine(path, "manifest.json"));
                this.ID = id;

                return new ImportPlanner(manifest, ExistingConfig.FromDatabase(PbxDatabase.Current), path, this.Options).Plan();
            }
            catch (MigrationArchiveException ex)
            {
                this.Error = ex.Message;
                return null;
            }
        }
    }
}
