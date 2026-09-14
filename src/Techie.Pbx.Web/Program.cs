using log4net;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

namespace Techie.Pbx.Web
{
    public class Program
    {
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

            builder.Services.AddAuthorization(options =>
            {
                // By default, all incoming requests will be authorized according to the default policy.
                options.FallbackPolicy = options.DefaultPolicy;
            });
            builder.Services.AddRazorPages()
                .AddMicrosoftIdentityUI();

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

            Log.Info("TNPBX web starting");
            app.Run();
        }
    }
}
