using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using log4net;

namespace Techie.Pbx.Core.Mail
{
    /// <summary>
    /// Sending through Microsoft Graph as a mailbox in the tenant, with this application's own
    /// Entra registration (D115) — the client credentials flow for a token, then one POST to
    /// <c>/users/{from}/sendMail</c>. No new app, no new credential, and no SDK: two HTTP calls
    /// are the whole protocol, and a package that pulls in half of MSAL to make them is surface
    /// area this project is explicitly trying not to have.
    ///
    /// Two things have to be true beyond having the credential, and neither can be arranged from
    /// here. The registration needs the <c>Mail.Send</c> <b>application</b> permission with admin
    /// consent, and <see cref="MailSettings.FromAddress"/> has to be a real mailbox in the tenant —
    /// a shared mailbox is the usual choice. Graph refuses clearly when either is missing, and
    /// those refusals are turned into sentences an admin can act on below.
    /// </summary>
    public class GraphMailSender
    {
        /// <summary>The scope a client-credentials token is asked for: the app's own permissions.</summary>
        public const string Scope = "https://graph.microsoft.com/.default";

        /// <summary>Where the message is posted. The placeholder is the sending mailbox.</summary>
        public const string SendMailUrlFormat = "https://graph.microsoft.com/v1.0/users/{0}/sendMail";

        /// <summary>Where the token comes from. The placeholder is the tenant.</summary>
        public const string TokenUrlFormat = "https://login.microsoftonline.com/{0}/oauth2/v2.0/token";

        /// <summary>
        /// One client for the process. Shared deliberately: a new HttpClient per send exhausts
        /// sockets, and this one holds no per-request state.
        /// </summary>
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        private static readonly ILog Log = LogManager.GetLogger(typeof(GraphMailSender));

        private readonly GraphCredential? credential;
        private readonly MailSettings settings;

        public GraphMailSender(MailSettings settings, GraphCredential? credential)
        {
            this.credential = credential;
            this.settings = settings;
        }

        /// <summary>
        /// Sends one HTML message, or says why it could not. Like the SMTP sender, nothing here
        /// throws at the caller: every way this fails is something an admin has to go and fix.
        /// </summary>
        public async Task<MailResult> SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken)
        {
            if (this.credential is not { IsComplete: true })
            {
                return MailResult.Failed(
                    "Microsoft Graph is not configured. It sends with this app's own Entra registration, which needs " +
                    "AzureAd:TenantId, AzureAd:ClientId and AzureAd:ClientSecret in appsettings — the secret is not in " +
                    "the repository and has to be set on this server. Use SMTP instead if this site has a relay.");
            }

            if (this.settings.FromAddress.Length == 0)
            {
                return MailResult.Failed(
                    "Graph sends as a mailbox, so Mail.FromAddress has to be set to a real mailbox in the tenant — " +
                    "a shared mailbox is the usual choice.");
            }

            var token = await this.TokenAsync(cancellationToken);

            if (token.Failure != null)
                return token.Failure;

            return await this.PostAsync(token.AccessToken!, to, subject, htmlBody, cancellationToken);
        }

        /// <summary>
        /// The message itself. Graph answers 202 with no body when it has accepted it for delivery,
        /// which is as far as this can ever know: acceptance is not the same as arrival, and the
        /// result says "accepted" rather than claiming more than that.
        ///
        /// No <c>from</c> is sent. The mailbox in the URL is the sender, and setting a different
        /// one needs SendAs rights that a Mail.Send permission does not grant — so on Graph the
        /// mailbox's own display name is what recipients see, and Mail.FromName does not apply.
        /// </summary>
        private async Task<MailResult> PostAsync(string accessToken, string to, string subject, string htmlBody, CancellationToken cancellationToken)
        {
            var mailbox = this.settings.FromAddress;
            var url = string.Format(SendMailUrlFormat, Uri.EscapeDataString(mailbox));

            var payload = JsonSerializer.Serialize(new
            {
                message = new
                {
                    subject,
                    body = new { contentType = "HTML", content = htmlBody },
                    toRecipients = new[] { new { emailAddress = new { address = to } } },
                },
                saveToSentItems = false,
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            try
            {
                using var response = await Http.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    Log.Info($"Mail accepted by Graph from {mailbox} to {to}");
                    return MailResult.Sent($"Graph accepted the message from {mailbox} for delivery to {to}.");
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var reason = Reason(body);

                Log.Warn($"Graph refused mail from {mailbox} to {to}: {(int)response.StatusCode} {reason}");

                // The two refusals that mean "somebody has to change something in Entra", named so
                // the admin is not left reading a Graph error code and guessing.
                var advice = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Forbidden =>
                        " This usually means the app registration has no Mail.Send application permission, or admin consent for it was never granted.",
                    System.Net.HttpStatusCode.NotFound =>
                        $" This usually means '{mailbox}' is not a mailbox in this tenant.",
                    _ => "",
                };

                return MailResult.Failed($"Graph refused the message ({(int)response.StatusCode}{Suffix(reason)}).{advice}");
            }
            catch (HttpRequestException ex)
            {
                Log.Warn($"Graph send to {to} failed: {ex.Message}");
                return MailResult.Failed("Could not reach Microsoft Graph. Check that this server has outbound HTTPS.");
            }
            catch (OperationCanceledException)
            {
                Log.Warn($"Graph send to {to} timed out");
                return MailResult.Failed("Microsoft Graph did not answer in time.");
            }
        }

        /// <summary>
        /// Whatever a Graph or Entra error response has to say about itself, as one short string.
        /// Both shapes are handled — Graph answers <c>{"error":{"code","message"}}</c> and the
        /// token endpoint answers <c>{"error","error_description"}</c>. Never the whole body: it
        /// goes into a message an admin reads, not a log of everything the server said.
        /// </summary>
        private static string Reason(string body)
        {
            try
            {
                using var json = JsonDocument.Parse(body);
                var root = json.RootElement;

                if (root.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.Object)
                    {
                        var code = error.TryGetProperty("code", out var c) ? c.GetString() : null;
                        var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;

                        return string.Join(": ", new[] { code, message }.Where(part => !string.IsNullOrWhiteSpace(part)));
                    }

                    var description = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;

                    return description ?? error.GetString() ?? "";
                }
            }
            catch (JsonException)
            {
                // Not JSON at all. Whatever it is, it is not ours to quote back.
            }

            return "";
        }

        private static string Suffix(string reason) => reason.Length > 0 ? ": " + reason : "";

        /// <summary>
        /// An app-only access token, by the client credentials flow. The secret goes into the form
        /// body and nowhere else — not into a log line, not into the message that comes back.
        /// </summary>
        private async Task<(string? AccessToken, MailResult? Failure)> TokenAsync(CancellationToken cancellationToken)
        {
            var url = string.Format(TokenUrlFormat, Uri.EscapeDataString(this.credential!.TenantId));

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = this.credential.ClientId,
                ["client_secret"] = this.credential.ClientSecret,
                ["grant_type"] = "client_credentials",
                ["scope"] = Scope,
            });

            try
            {
                using var response = await Http.PostAsync(url, form, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var reason = Reason(body);

                    Log.Warn($"Entra refused an app-only token for Graph mail: {(int)response.StatusCode} {reason}");
                    return (null, MailResult.Failed(
                        $"Entra would not issue a token for this app ({(int)response.StatusCode}{Suffix(reason)}). " +
                        "Check AzureAd:ClientId, AzureAd:TenantId and AzureAd:ClientSecret on this server."));
                }

                using var json = JsonDocument.Parse(body);

                if (!json.RootElement.TryGetProperty("access_token", out var token) || token.GetString() is not { Length: > 0 } value)
                {
                    Log.Warn("Entra answered the token request without an access token");
                    return (null, MailResult.Failed("Entra answered the token request without a token in it."));
                }

                return (value, null);
            }
            catch (HttpRequestException ex)
            {
                Log.Warn($"Could not reach Entra for a Graph mail token: {ex.Message}");
                return (null, MailResult.Failed("Could not reach Entra to get a token. Check that this server has outbound HTTPS."));
            }
            catch (JsonException)
            {
                Log.Warn("Entra answered the token request with something that was not JSON");
                return (null, MailResult.Failed("Entra answered the token request with something this could not read."));
            }
            catch (OperationCanceledException)
            {
                Log.Warn("The Entra token request for Graph mail timed out");
                return (null, MailResult.Failed("Entra did not answer the token request in time."));
            }
        }
    }
}
