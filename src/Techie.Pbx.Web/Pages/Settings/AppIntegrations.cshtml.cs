using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Pages.Settings;

/// <summary>
/// Guide for adding the PBX dialer as a Teams tab app. The admin types the three values the
/// Developer Portal and Entra hand out — the app registration's client ID, the app package's
/// own manifest UUID and the public host the dialer is served from — and they are stored as
/// the TeamsApp.* settings. The downloadable package is built from the shipped manifest
/// template with those values filled in, so nothing has to be edited in the deployment (D182).
/// </summary>
public partial class AppIntegrationsModel : PageModel
{
    private static readonly string TemplateDirectory =
        System.IO.Path.Combine(AppContext.BaseDirectory, "TeamsTab");

    private readonly SettingsRepository settings;

    /// <summary>The Entra app registration's application (client) ID.</summary>
    [BindProperty]
    public string? ClientId { get; set; }

    /// <summary>The Teams app package's own UUID, the manifest's top-level id.</summary>
    [BindProperty]
    public string? ManifestId { get; set; }

    /// <summary>The public host the Teams tab loads the dialer from, e.g. pbx.example.com.</summary>
    [BindProperty]
    public string? SiteUrl { get; set; }

    /// <summary>The Entra tenant the app registration lives in — the sign-in authority, optional.</summary>
    [BindProperty]
    public string? TenantId { get; set; }

    public AppIntegrationsModel()
    {
        this.settings = new SettingsRepository(PbxDatabase.Current);
    }

    /// <summary>True when every field the manifest needs has a value stored.</summary>
    public bool Ready => Guid.TryParse(this.LoadedClientId, out _) &&
        Guid.TryParse(this.LoadedManifestId, out _) &&
        HostPattern().IsMatch(this.LoadedSiteUrl ?? "");

    private string? LoadedClientId { get; set; }

    private string? LoadedManifestId { get; set; }

    private string? LoadedSiteUrl { get; set; }

    private string? LoadedTenantId { get; set; }

    /// <summary>
    /// The manifest with the stored values filled in. A value nobody has stored leaves the
    /// template's placeholder in place; the page says what is still missing.
    /// </summary>
    private string Manifest
    {
        get
        {
            var node = JsonNode.Parse(
                System.IO.File.ReadAllText(System.IO.Path.Combine(TemplateDirectory, "manifest.json")))!;

            node["id"] = Guid.TryParse(this.LoadedManifestId, out _) ? this.LoadedManifestId : node["id"]!.ToString();

            var webApp = node["webApplicationInfo"]!;
            webApp["id"] = Guid.TryParse(this.LoadedClientId, out _) ? this.LoadedClientId : webApp["id"]!.ToString();
            webApp["resource"] = $"api://{this.LoadedSiteUrl ?? HostFromTemplate(webApp)}/{webApp["id"]}";

            var tab = node["staticTabs"]![0]!;
            tab["contentUrl"] = $"https://{this.LoadedSiteUrl ?? HostFromTemplate(tab)}/phone";
            tab["websiteUrl"] = tab["contentUrl"]!.ToString();

            node["validDomains"] = new JsonArray(this.LoadedSiteUrl ?? HostFromTemplate(tab));

            return node.ToString();
        }
    }

    public void OnGet() => this.Load();

    /// <summary>Reads the stored TeamsApp.* values into the fields and the Loaded* copies.</summary>
    private void Load()
    {
        var stored = this.settings.GetAll();
        this.LoadedClientId = this.ClientId = stored.GetValueOrDefault(SettingsKeys.TeamsAppClientId);
        this.LoadedManifestId = this.ManifestId = stored.GetValueOrDefault(SettingsKeys.TeamsAppManifestId);
        this.LoadedSiteUrl = this.SiteUrl = stored.GetValueOrDefault(SettingsKeys.TeamsAppSiteUrl);
        this.LoadedTenantId = this.TenantId = stored.GetValueOrDefault(SettingsKeys.TeamsAppTenantId);
    }

    /// <summary>Saves the values the admin typed, clearing any they blanked, then re-shows the page.</summary>
    public IActionResult OnPost()
    {
        var keys = new Dictionary<string, string?>
        {
            [SettingsKeys.TeamsAppClientId] = this.ClientId?.Trim(),
            [SettingsKeys.TeamsAppManifestId] = this.ManifestId?.Trim(),
            [SettingsKeys.TeamsAppSiteUrl] = this.SiteUrl?.Trim(),
            [SettingsKeys.TeamsAppTenantId] = this.TenantId?.Trim(),
        };

        foreach (var (key, value) in keys)
        {
            var errors = value is { Length: > 0 } ? SettingsValidation.Errors(key, value) : [];
            foreach (var error in errors)
            {
                this.ModelState.AddModelError(string.Empty, error);
            }
        }

        if (!this.ModelState.IsValid)
        {
            this.OnGet();
            return this.Page();
        }

        foreach (var (key, value) in keys)
        {
            if (string.IsNullOrEmpty(value))
            {
                this.settings.Delete(key);
            }
            else
            {
                this.settings.Set(key, value);
            }
        }

        return this.RedirectToPage(new { saved = true });
    }

    /// <summary>
    /// Serves the Teams app package: the manifest built from the settings plus the icons,
    /// zipped under the names the manifest itself references.
    /// </summary>
    public IActionResult OnGetPackage()
    {
        if (!System.IO.Directory.Exists(TemplateDirectory))
        {
            return this.NotFound();
        }

        // A named handler does not run OnGet, so the stored values must be loaded here too.
        this.Load();
        var manifest = this.Manifest;

        using var zipStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            foreach (var (name, content) in new Dictionary<string, byte[]>
            {
                ["manifest.json"] = System.Text.Encoding.UTF8.GetBytes(manifest),
            })
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }

            foreach (var name in new[] { "color.png", "outline.png" })
            {
                var file = System.IO.Path.Combine(TemplateDirectory, name);
                if (!System.IO.File.Exists(file))
                {
                    continue;
                }

                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                using var fileStream = System.IO.File.OpenRead(file);
                fileStream.CopyTo(entryStream);
            }
        }

        return this.File(zipStream.ToArray(), "application/zip", "techie-pbx-teams.zip");
    }

    /// <summary>The host the template carries, for fields the admin has not filled in yet.</summary>
    private static string HostFromTemplate(JsonNode node)
    {
        var text = node["contentUrl"]?.ToString() ?? node["resource"]?.ToString() ?? "";
        var host = text.Contains("://") ? new Uri(text).Host : text.Split('/')[0];
        return host.Length > 0 ? host : "pbx.example.com";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*$")]
    private static partial System.Text.RegularExpressions.Regex HostPattern();
}
