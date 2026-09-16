using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Routes
{
    /// <summary>
    /// The outbound routes page: which numbers go out, and over which trunk. Built like the other
    /// list pages — a shell htmx fills, forms in the shared Bootstrap modal (D42) — with no live
    /// status to poll, because a route is a rule rather than a connection.
    /// </summary>
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly OutboundRouteRepository routes;
        private readonly TrunkRepository trunks;

        public IndexModel()
        {
            this.routes = new OutboundRouteRepository(PbxDatabase.Current);
            this.trunks = new TrunkRepository(PbxDatabase.Current);
        }

        public void OnGet()
        {
        }

        /// <summary>The create or edit form, which the page shows in the Bootstrap modal.</summary>
        public IActionResult OnGetForm(long? outboundRouteID)
        {
            if (outboundRouteID is null or 0)
                return this.Partial("_Form", new RouteForm { Trunks = this.UsableTrunks() });

            var route = this.routes.GetByID(outboundRouteID.Value);
            if (route == null)
                return this.NotFound();

            return this.Partial("_Form", new RouteForm
            {
                DialPattern = route.DialPattern,
                Enabled = route.Enabled,
                Name = route.Name,
                OutboundRouteID = route.OutboundRouteID,
                Priority = route.Priority,
                TrunkID = route.TrunkID,
                Trunks = this.UsableTrunks(),
            });
        }

        /// <summary>The whole table, in the order the routes are tried.</summary>
        public PartialViewResult OnGetTable()
        {
            var all = this.trunks.GetAll();

            var rows = this.routes.GetAll()
                .Select(route =>
                {
                    var trunk = all.FirstOrDefault(t => t.TrunkID == route.TrunkID);

                    return new RouteRow
                    {
                        DialPattern = route.DialPattern,
                        Enabled = route.Enabled,
                        Name = route.Name,
                        OutboundRouteID = route.OutboundRouteID,
                        Priority = route.Priority,
                        Trunk = trunk?.Name ?? "(trunk deleted)",
                        TrunkUsable = trunk is { Enabled: true },
                    };
                })
                .ToList();

            return this.Partial("_Table", rows);
        }

        public IActionResult OnPostDelete(long outboundRouteID)
        {
            var route = this.routes.GetByID(outboundRouteID);
            if (route == null)
                return this.NotFound();

            this.routes.Delete(outboundRouteID);

            Log.Info($"Outbound route {route.Name} deleted by {this.User.Identity?.Name}");
            return this.Changed($"Route {route.Name} deleted.");
        }

        /// <summary>
        /// Creates or updates one route. Validation failures come back as the form again, with the
        /// repository's messages on it — including the international guard, which is the one an
        /// admin is most likely to meet (D47).
        /// </summary>
        public IActionResult OnPostSave(RouteForm form)
        {
            var isNew = form.OutboundRouteID == 0;
            var route = isNew ? new OutboundRoute() : this.routes.GetByID(form.OutboundRouteID);
            if (route == null)
                return this.NotFound();

            route.DialPattern = Text(form.DialPattern);
            route.Enabled = form.Enabled;
            route.Name = Text(form.Name);
            route.Priority = form.Priority;
            route.TrunkID = form.TrunkID;

            try
            {
                if (isNew)
                    this.routes.Insert(route);
                else
                    this.routes.Update(route);
            }
            catch (ValidationFailedException ex)
            {
                form.Errors = ex.Errors.ToList();
                form.Trunks = this.UsableTrunks();
                return this.Partial("_Form", form);
            }

            Log.Info($"Outbound route {route.Name} {(isNew ? "created" : "updated")} by {this.User.Identity?.Name}");
            return this.Changed($"Route {route.Name} saved.");
        }

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to.
        /// "routesChanged" refreshes the table, "configChanged" wakes the navbar's apply button
        /// (D43), "pbxToast" says what happened.
        /// </summary>
        private IActionResult Changed(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["routesChanged"] = null,
                ["configChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        private List<Trunk> UsableTrunks() =>
            this.trunks.GetAll().Where(t => t.Enabled).ToList();
    }
}
