using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Diagnostics;

namespace Techie.Pbx.Web.Pages.Status
{
    /// <summary>
    /// The Logs page: a read-only tail of one of the named logs in <see cref="LogSources"/>.
    /// The browser only ever posts a name from that list, never a path — <see cref="OnGetLines"/>
    /// is the only place a name turns into a file, and a name that is not on the list is a 400,
    /// never a guess at a path.
    /// </summary>
    public class LogsModel : PageModel
    {
        /// <summary>The line counts the dropdown offers, and the only ones the handler accepts.</summary>
        private static readonly int[] AllowedLineCounts = { 100, 250, 1000 };

        private const int DefaultLines = 250;

        private static readonly ILog Log = LogManager.GetLogger(typeof(LogsModel));

        private const int MaxFilterLength = 100;

        /// <summary>
        /// How much of a file is scanned before filtering and taking the tail. Bounds a poll
        /// against a large messages.log to one seek and this many bytes, not the whole file.
        /// </summary>
        private const int MaxScanBytes = 4 * 1024 * 1024;

        private readonly SettingsRepository settings;

        public LogsModel()
        {
            this.settings = new SettingsRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>
        /// One poll of the viewer: the header line and the matching tail of one named log. Always
        /// a partial, never the full page — see ConfigStatusModel.OnGet for why a handler that
        /// only ever answers an htmx poll must not render the layout.
        /// </summary>
        public IActionResult OnGetLines(string? source, int lines, string? filter)
        {
            var logSource = LogSources.Find(source);
            if (logSource == null)
                return this.BadRequest();

            var view = new LogLinesView
            {
                Filter = Filter(filter),
                Lines = AllowedLineCounts.Contains(lines) ? lines : DefaultLines,
                Source = logSource,
            };

            view.Path = logSource.Resolve(this.settings.GetAll());
            if (view.Path == null)
                return this.Partial("_Lines", view);

            view.Result = LogTail.Read(view.Path, view.Lines, view.Filter, MaxScanBytes);

            // Never for "does not exist yet" - that is the ordinary state of a log nobody has
            // written to, and the page already says so without anything in the log for it.
            if (view.Result.Error != null)
                Log.Warn($"Log source '{logSource.Name}' could not be read: {view.Result.Error}");

            return this.Partial("_Lines", view);
        }

        /// <summary>Trimmed, capped to <see cref="MaxFilterLength"/>, and empty means none.</summary>
        private static string? Filter(string? value)
        {
            var text = (value ?? "").Trim();
            if (text.Length > MaxFilterLength)
                text = text[..MaxFilterLength];

            return text.Length == 0 ? null : text;
        }
    }
}
