using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Techie.Pbx.Web.Pages.Settings;

/// <summary>
/// Guide for adding the PBX dialer as a Teams tab app. Steps the admin through
/// the Developer Portal setup, then serves a ready-to-upload app package zip
/// assembled from the TeamsTab assets shipped with the app.
/// </summary>
public class AppIntegrationsModel : PageModel
{
    private static readonly Guid PlaceholderId = new("00000000-0000-0000-0000-000000000000");

    /// <summary>True when the shipped manifest still carries the spike placeholder UUID.</summary>
    public bool ManifestNeedsId { get; private set; }

    public void OnGet()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "TeamsTab", "manifest.json");
        ManifestNeedsId = System.IO.File.Exists(path)
            && System.IO.File.ReadAllText(path).Contains(PlaceholderId.ToString());
    }

    /// <summary>Serves the Teams app package: manifest + icons zipped, same names the manifest references.</summary>
    public IActionResult OnGetPackage()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "TeamsTab");
        if (!System.IO.Directory.Exists(dir))
        {
            return NotFound();
        }

        using var zipStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            foreach (var name in new[] { "manifest.json", "color.png", "outline.png" })
            {
                var file = System.IO.Path.Combine(dir, name);
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

        return File(zipStream.ToArray(), "application/zip", "techie-pbx-teams.zip");
    }
}
