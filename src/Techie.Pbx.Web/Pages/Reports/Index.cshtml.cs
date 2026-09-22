using System.Text;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;
using Techie.Pbx.Core.Reports;

namespace Techie.Pbx.Web.Pages.Reports
{
    /// <summary>
    /// Call reports (F5): the records the collector stored, filtered by day, extension, direction
    /// and outcome, with totals per extension and per trunk and a CSV of the same selection.
    ///
    /// The filters are a plain GET form, so a report is a URL. htmx asks the Results handler again
    /// whenever one changes; the export button submits the same form to the Export handler, so the
    /// CSV is always exactly the selection on screen. Days are the site's (the System.Timezone
    /// setting, D74), which is also the zone every time on the page is shown in.
    /// </summary>
    public class IndexModel : PageModel
    {
        /// <summary>
        /// The most rows the list renders. Records are kept forever, and a year of a busy office
        /// is more HTML than a browser wants; the totals and the CSV still cover everything.
        /// </summary>
        public const int MaxRows = 2000;

        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        /// <summary>The extensions the filter offers, by number.</summary>
        public List<Extension> Extensions { get; private set; } = new();

        /// <summary>The filters, as the query string had them or as they default.</summary>
        public ReportFilterForm Form { get; private set; } = new();

        /// <summary>Today in the site's zone: what the quick date buttons are worked out from.</summary>
        public DateOnly SiteToday { get; private set; }

        /// <summary>The site's zone, named on the page so the times are not a guess.</summary>
        public string Timezone { get; private set; } = "";

        private readonly CdrRepository cdrs;
        private readonly ExtensionRepository extensions;
        private readonly SettingsRepository settings;
        private readonly TrunkRepository trunks;

        public IndexModel()
        {
            this.cdrs = new CdrRepository(PbxDatabase.Current);
            this.extensions = new ExtensionRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
            this.trunks = new TrunkRepository(PbxDatabase.Current);
        }

        public void OnGet([FromQuery] ReportFilterForm form)
        {
            var (zone, name) = this.Zone();

            this.SiteToday = Today(zone);
            form.Default(this.SiteToday);
            this.Form = form;
            this.Extensions = this.extensions.GetAll().OrderBy(e => e.Number.Length).ThenBy(e => e.Number, StringComparer.Ordinal).ToList();
            this.Timezone = name;
        }

        /// <summary>One record in full, for the detail modal a click on a row opens.</summary>
        public IActionResult OnGetDetail(long cdrID)
        {
            var cdr = this.cdrs.GetByID(cdrID);
            if (cdr == null)
                return this.NotFound();

            var (zone, name) = this.Zone();

            return this.Partial("_Detail", new CallDetail
            {
                Cdr = cdr,
                Row = CallRow.For(cdr, this.TrunkNames(), zone),
                Timezone = name,
                Zone = zone,
            });
        }

        /// <summary>
        /// The same selection as the list, as a CSV download. Not a partial: the export button is a
        /// plain form submit, and the browser saves what comes back.
        /// </summary>
        public IActionResult OnGetExport([FromQuery] ReportFilterForm form)
        {
            var (zone, _) = this.Zone();
            var filter = form.ToFilter(zone, Today(zone), out var errors);

            if (filter == null)
                return this.BadRequest(string.Join(" ", errors));

            var trunkNames = this.TrunkNames();
            var found = this.cdrs.Find(filter, trunkNames);
            var csv = CdrCsv.Render(found, trunkNames);

            Log.Info($"Call report exported by {this.User.Identity?.Name}: {found.Count} records, {form.From:yyyy-MM-dd} to {form.To:yyyy-MM-dd}");

            return this.File(
                Encoding.UTF8.GetBytes(csv),
                "text/csv; charset=utf-8",
                $"calls-{form.From:yyyyMMdd}-{form.To:yyyyMMdd}.csv");
        }

        /// <summary>The list and the totals for the filters, which htmx swaps in under the form.</summary>
        public PartialViewResult OnGetResults([FromQuery] ReportFilterForm form)
        {
            var (zone, _) = this.Zone();
            var filter = form.ToFilter(zone, Today(zone), out var errors);

            if (filter == null)
                return this.Partial("_Results", new ReportResults { Errors = errors });

            var trunkNames = this.TrunkNames();
            var found = this.cdrs.Find(filter, trunkNames);
            var (extensionTotals, trunkTotals) = CdrTotals.For(found, trunkNames);

            return this.Partial("_Results", new ReportResults
            {
                ExtensionTotals = extensionTotals,
                Matched = found.Count,
                Rows = found.Take(MaxRows).Select(cdr => CallRow.For(cdr, trunkNames, zone)).ToList(),
                TrunkTotals = trunkTotals,
            });
        }

        private static DateOnly Today(TimeZoneInfo zone) =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));

        private IReadOnlySet<string> TrunkNames() =>
            this.trunks.GetAll().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        /// <summary>
        /// The site's zone and its name. A zone this machine cannot find falls back to UTC rather
        /// than failing the page, for the reason AsteriskSettings.Timezone falls back: the settings
        /// page is what reports a bad value.
        /// </summary>
        private (TimeZoneInfo Zone, string Name) Zone()
        {
            var name = AsteriskSettings.Timezone(this.settings.GetAll());

            try
            {
                return (TimeZoneInfo.FindSystemTimeZoneById(name), name);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return (TimeZoneInfo.Utc, "UTC");
            }
        }
    }
}
