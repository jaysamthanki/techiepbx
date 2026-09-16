using log4net;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

namespace Techie.Pbx.Web
{
    public class Program
    {
        /// <summary>
        /// The header htmx and our own fetch calls send the antiforgery token in. The layout puts
        /// the token on the body element (hx-headers) and in a meta tag.
        /// </summary>
        public const string AntiforgeryHeaderName = "RequestVerificationToken";

        /// <summary>Where the installer puts the database. Override with Database:Path.</summary>
        public const string DefaultDatabasePath = "/var/lib/tnpbx/tnpbx.db";

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

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapRazorPages()
               .WithStaticAssets();
            app.MapControllers();

            PbxDatabase.Open(app.Configuration["Database:Path"] ?? DefaultDatabasePath);

            Log.Info("TNPBX web starting");
            app.Run();
        }
    }
}
