using Pagr.Sdk.Exceptions;
using Pagr.Sdk.Models;

namespace Pagr.Sdk.Examples;

/// <summary>Shared lookups for the examples.</summary>
internal static class ExampleData
{
    /// <summary>
    /// Picks the first template that has a published version, together with that latest
    /// published version. Only published versions can be fetched via <c>latest</c> and
    /// rendered — a template's <c>LatestVersionNumber</c> also counts unpublished drafts,
    /// so the only reliable check is asking for the latest published version and skipping
    /// templates that report none.
    /// </summary>
    public static async Task<(Template Template, TemplateVersion Version)?> PickPublishedTemplateAsync(
        PagrApiClient client)
    {
        var page = await client.GetTemplatesAsync(new ListOptions { Take = 50 });
        foreach (var template in page)
        {
            try
            {
                var version = await client.GetTemplateVersionAsync(template.Id); // latest published
                Console.WriteLine($"Using template: {template.Name} (v{version.VersionNumber})");
                return (template, version);
            }
            catch (PagrNotFoundException)
            {
                // This template has no published version — try the next one.
            }
        }

        Console.WriteLine(
            page.Total == 0
                ? "No templates in this organisation — create one first."
                : $"None of the first {page.Count} template(s) has a published version — publish one first.");
        return null;
    }
}
