using log4net;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Techie.Pbx.Web.Security;

namespace Techie.Pbx.Web
{
    public class Program
    {
        /// <summary>
        /// The header htmx and our own fetch calls send the antiforgery token in. The layout puts
        /// the token on the body element (hx-headers) and in a meta tag.
        /// </summary>
        public const string AntiforgeryHeaderName = "RequestVerificationToken";

        /// <summary>
        /// Where the database goes when Database:Path says nothing: a Data folder inside the
        /// install, so that copying the app folder copies everything it owns (D25).
        /// </summary>
        public const string DefaultDatabasePath = "Data/tnpbx.db";

        private static readonly ILog Log = LogManager.GetLogger(typeof(Program));

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Route framework logging into log4net; our own code logs via LogManager directly.
            builder.Logging.ClearProviders();
            builder.Logging.AddLog4Net("log4net.config");

            // Cookie-based Entra ID sign-in. API controllers use the same cookie (only our Razor pages call them).
            builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

            // An unauthenticated API or htmx call must not be answered with a redirect to Entra:
            // the browser cannot follow it from fetch, so the caller sees a CORS failure instead
            // of "your session ended" (D20).
            builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                var redirectToIdentityProvider = options.Events.OnRedirectToIdentityProvider;

                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    var request = context.Request;
                    var isApi = request.Path.StartsWithSegments("/api");
                    var isHtmx = request.Headers.ContainsKey("HX-Request");

                    if (!isApi && !isHtmx)
                        return redirectToIdentityProvider(context);

                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                    // Let htmx reload the page, which does sign in properly, as a full navigation.
                    if (isHtmx)
                        context.Response.Headers["HX-Refresh"] = "true";

                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

            builder.Services.AddAuthorization(options =>
            {
                // By default, all incoming requests will be authorized according to the default policy.
                options.FallbackPolicy = options.DefaultPolicy;
            });
            builder.Services.AddRazorPages()
                .AddMicrosoftIdentityUI();

            // The API is called by our own pages with the session cookie, so it needs the same
            // antiforgery protection as a form post.
            builder.Services.AddAntiforgery(options => options.HeaderName = AntiforgeryHeaderName);
            builder.Services.AddControllers(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            // Everything an admin touches is HTTPS. Phone provisioning is the exception: DHCP
            // option 160 (Polycom) and option 66 (Yealink, D88) each point a phone at a URL with a
            // scheme in it, and a phone that was pointed at http:// has to be answered rather than
            // redirected somewhere it may have no certificate store for (D77). The credentials are
            // the gate either way.
            app.UseWhen(
                context => !context.Request.Path.StartsWithSegments(Controllers.PolycomController.RoutePrefix) &&
                           !context.Request.Path.StartsWithSegments(Controllers.YealinkController.RoutePrefix),
                branch => branch.UseHttpsRedirection());

            app.UseRouting();

            app.UseAuthentication();

            // The failsafe sign-in, and only when it has been asked for (D24).
            var bypass = LocalBypassSettings.FromConfiguration(app.Configuration);
            if (bypass.Enabled)
            {
                app.UseMiddleware<LocalBypassMiddleware>(bypass);

                if (bypass.AllowedNetworks.Networks.Count == 0)
                    Log.Warn("Local sign-in bypass is enabled but no networks are allowed, so it can never apply");
                else
                    Log.Warn($"Local sign-in bypass is ENABLED: requests from {bypass.AllowedNetworks} are admins without signing in");
            }

            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapRazorPages()
               .WithStaticAssets();
            app.MapControllers();

            PbxDatabase.Open(DatabasePath(app));
            PbxSounds.Open(app.Configuration, app.Environment.ContentRootPath);

            Log.Info("TNPBX web starting");
            app.Run();
        }

        /// <summary>
        /// Database:Path, or the default, and a relative path is relative to the install rather
        /// than to whatever directory the service happened to start in.
        /// </summary>
        private static string DatabasePath(WebApplication app)
        {
            var configured = app.Configuration["Database:Path"];
            var path = string.IsNullOrWhiteSpace(configured) ? DefaultDatabasePath : configured.Trim();

            return Path.IsPathRooted(path) ? path : Path.Combine(app.Environment.ContentRootPath, path);
        }
    }
}
