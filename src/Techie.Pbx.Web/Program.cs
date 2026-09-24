using System.Security.Cryptography.X509Certificates;
using log4net;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Diagnostics;
using Techie.Pbx.Core.Security;
using Techie.Pbx.Web.CallRecords;
using Techie.Pbx.Web.Certificates;
using Techie.Pbx.Web.Security;

namespace Techie.Pbx.Web
{
    public class Program
    {
        /// <summary>
        /// Where an ACME server fetches the answer to an HTTP-01 challenge. Plain HTTP, no
        /// authentication and no redirect: the path is fixed by the ACME standard and the token in
        /// it is the only thing that makes an answer available at all (D98).
        /// </summary>
        public const string AcmeChallengePath = "/.well-known/acme-challenge";

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

            // Before anything else now: which ports this app listens on is a question only the
            // database can answer, because the certificate lives there (D97, D99).
            PbxDatabase.Open(DatabasePath(builder.Environment, builder.Configuration));

            // Whether there is a web request log, and where it goes, is the other question only the
            // database can answer before the pipeline is built (D116).
            PbxRequestLog.Open(builder.Environment.ContentRootPath, new SettingsRepository(PbxDatabase.Current).GetAll());

            if (PbxRequestLog.Enabled)
                builder.Services.AddW3CLogging(RequestLogOptions);

            var (certificate, chain) = ServerCertificate();
            var bindings = WebBindings.For(certificate != null);

            builder.WebHost.ConfigureKestrel(options =>
            {
                foreach (var binding in bindings)
                {
                    if (binding.UsesCertificate && certificate != null)
                        options.ListenAnyIP(binding.Port, listen => listen.UseHttps(https => Present(https, certificate, chain)));
                    else
                        options.ListenAnyIP(binding.Port);
                }
            });

            // Cookie-based Entra ID sign-in. API controllers use the same cookie (only our Razor pages call them).
            builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

            // The auth cookie, the OIDC correlation cookies and the antiforgery tokens are all
            // encrypted with this key ring, so it must survive restarts and re-deploys: a fresh
            // key per process would sign everyone out mid-flow — and an OIDC flow that straddles
            // a restart lands on the error page with a state it cannot read. The ring lives
            // beside the database, inside the deploy target (D95): appsettings and Data/ are the
            // server's live state, and this is the same kind of thing. Keys expire after a year
            // (user decision): an appliance renews them in the background, and a year is long
            // enough that a re-deploy never loses the ring, short enough that a key does not
            // outlive its usefulness.
            builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(
                    Path.Combine(builder.Environment.ContentRootPath, "Data", "keys")))
                .SetDefaultKeyLifetime(TimeSpan.FromDays(365));

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

            // Renews certificates before they expire, whether or not anybody is signed in (D100).
            builder.Services.AddHostedService<CertificateRenewalService>();

            // Listens on AMI for call records and stores them for the reports page (F5).
            builder.Services.AddHostedService<CdrCollectorService>();

            if (certificate != null)
                builder.Services.AddHttpsRedirection(options => options.HttpsPort = WebBindings.HttpsPort);

            var app = builder.Build();

            // First in the pipeline, so exactly one line is written for every request whatever
            // becomes of it further in: the HTTPS redirect, a 401 from Entra, an unhandled
            // exception the error page turns into a 500. It wraps authentication rather than
            // sitting behind it, which is the whole point for provisioning — a phone has no
            // session, and "did the phone even reach us, and what did we answer" is the question
            // this log exists to answer (D116).
            if (PbxRequestLog.Enabled)
                app.UseW3CLogging();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            // Everything an admin touches is HTTPS, once there is a certificate to serve it with;
            // before that there is nothing to redirect to and the branch is not added at all (D99).
            //
            // Two exceptions either way. Phone provisioning: DHCP option 160 (Polycom) and option
            // 66 (Yealink, D88) each point a phone at a URL with a scheme in it, and a phone that
            // was pointed at http:// has to be answered rather than redirected somewhere it may
            // have no certificate store for (D77). And the ACME challenge, which is fetched over
            // plain HTTP on port 80 by definition — redirecting it would break the renewal that
            // keeps the certificate alive (D98). Only port 80 redirects (D99): 8080 is the
            // HTTP way back in — an IP address or a hostname the certificate does not cover must
            // keep working over plain HTTP there, or the redirect locks an admin out.
            if (certificate != null)
            {
                app.UseWhen(
                    context => context.Connection.LocalPort == WebBindings.HttpPort &&
                               !context.Request.Path.StartsWithSegments(Controllers.PolycomController.RoutePrefix) &&
                               !context.Request.Path.StartsWithSegments(Controllers.YealinkController.RoutePrefix) &&
                               !context.Request.Path.StartsWithSegments(AcmeChallengePath),
                    branch => branch.UseHttpsRedirection());
            }

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

            // Names a provisioning request after the Basic username it carried, for the request
            // log's benefit only — after authentication, because that middleware replaces the user
            // whenever a scheme returns one, and before authorization, which is untouched by an
            // identity that is not authenticated (D116).
            if (PbxRequestLog.Enabled)
                app.UseMiddleware<RequestLogUserMiddleware>();

            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapRazorPages()
               .WithStaticAssets();
            app.MapControllers();
            MapAcmeChallenge(app);

            PbxSounds.Open(app.Configuration, app.Environment.ContentRootPath);
            PbxEntra.Open(app.Configuration);

            // The relay details the voicemail mailcmd script reads, refreshed from the settings on
            // every start so a re-deploy cannot leave voicemail email broken in silence (D126).
            PbxMailConfig.Open(app.Environment.ContentRootPath, new SettingsRepository(PbxDatabase.Current));

            Log.Info($"TNPBX web {typeof(Program).Assembly.GetName().Version} starting on {string.Join(", ", bindings)}");
            app.Run();
        }

        /// <summary>
        /// Database:Path, or the default, and a relative path is relative to the install rather
        /// than to whatever directory the service happened to start in.
        /// </summary>
        private static string DatabasePath(IWebHostEnvironment environment, IConfiguration configuration)
        {
            var configured = configuration["Database:Path"];
            var path = string.IsNullOrWhiteSpace(configured) ? DefaultDatabasePath : configured.Trim();

            return Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path);
        }

        /// <summary>
        /// The one endpoint outside the Entra cookie that is not phone provisioning: the ACME
        /// server fetching the answer to a challenge this app published while ordering (D98).
        /// Anonymous because the ACME server has no credentials to offer and the token is the
        /// secret; a token nobody is waiting for gets a 404, so there is nothing here to walk.
        /// </summary>
        private static void MapAcmeChallenge(WebApplication app)
        {
            app.MapGet(AcmeChallengePath + "/{token}", (string token) =>
            {
                var answer = AcmeChallengeStore.Answer(token);

                if (answer == null)
                {
                    Log.Warn($"ACME challenge for an unknown token refused");
                    return Results.NotFound();
                }

                Log.Info("ACME challenge answered");
                return Results.Text(answer, "text/plain");
            }).AllowAnonymous();
        }

        /// <summary>
        /// What the HTTPS listener presents: the leaf with its private key, plus any issuers, so a
        /// client that does not already hold the intermediate can still build a chain.
        /// </summary>
        private static void Present(HttpsConnectionAdapterOptions https, X509Certificate2 certificate, X509Certificate2Collection chain)
        {
            https.ServerCertificate = certificate;

            if (chain.Count > 0)
                https.ServerCertificateChain = chain;
        }

        /// <summary>
        /// What the W3C request log records, and how it rolls (D116). The columns are as close to
        /// an Apache combined log as this logger goes: the time, who asked, which port they asked
        /// on, what they asked for, what they got, how long it took, and the two headers that say
        /// which client it was and where it came from.
        ///
        /// Two deliberate absences. <c>cs(Cookie)</c> is not logged, because our cookie is the Entra
        /// session itself and a log full of session cookies is a log full of credentials. And there
        /// is no <c>sc-bytes</c>: ASP.NET Core's W3C logger has no bytes-sent field at all, so it is
        /// the one Apache column that cannot be had here.
        /// </summary>
        private static void RequestLogOptions(W3CLoggerOptions options)
        {
            options.LoggingFields =
                W3CLoggingFields.Date |
                W3CLoggingFields.Time |
                W3CLoggingFields.ClientIpAddress |
                W3CLoggingFields.UserName |
                W3CLoggingFields.ServerPort |
                W3CLoggingFields.Method |
                W3CLoggingFields.UriStem |
                W3CLoggingFields.UriQuery |
                W3CLoggingFields.ProtocolStatus |
                W3CLoggingFields.TimeTaken |
                W3CLoggingFields.ProtocolVersion |
                W3CLoggingFields.Host |
                W3CLoggingFields.UserAgent |
                W3CLoggingFields.Referer;

            options.FileName = RequestLog.FileNamePrefix;
            options.FileSizeLimit = RequestLog.FileSizeLimitBytes;
            options.FlushInterval = TimeSpan.FromSeconds(RequestLog.FlushSeconds);
            options.LogDirectory = PbxRequestLog.DirectoryPath;
            options.RetainedFileCountLimit = RequestLog.RetainedFileCount;
        }

        /// <summary>
        /// The certificate Kestrel should serve, from the database (D99), or nulls to start in
        /// bootstrap mode. A row that cannot be loaded is logged and treated as no certificate at
        /// all: an appliance that will not start is worse than one that starts without HTTPS and
        /// says so.
        /// </summary>
        private static (X509Certificate2? Certificate, X509Certificate2Collection Chain) ServerCertificate()
        {
            var empty = new X509Certificate2Collection();
            var row = new CertificateRepository(PbxDatabase.Current).Current(DateTimeOffset.UtcNow);

            if (row == null)
            {
                Log.Info("No usable certificate: starting in bootstrap mode, HTTP only");
                return (null, empty);
            }

            var loaded = CertificatePem.Load(row.CertificatePem, row.KeyPem);

            if (loaded == null)
            {
                Log.Error($"Certificate '{row.Name}' could not be loaded; starting without HTTPS");
                return (null, empty);
            }

            KestrelCertificate.Loaded(row.CertificateID, row.ExpiresUtc);
            Log.Info($"Serving certificate '{row.Name}' for {row.Hostnames}, expires {row.ExpiresUtc}");

            return (loaded, CertificatePem.LoadChain(row.ChainPem));
        }
    }
}
