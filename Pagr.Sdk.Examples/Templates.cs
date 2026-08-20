namespace Pagr.Sdk.Examples;

/// <summary>Browsing templates and versions: paging, search, filters, document-name template, preview image.</summary>
internal static class Templates
{
    public static async Task RunAsync()
    {
        using var client = ExampleEnv.CreateClient();
        if (client is null)
            return;

        // Page through templates two at a time.
        Console.WriteLine("--- Paging ---");
        var skip = 0;
        while (true)
        {
            var page = await client.GetTemplatesAsync(new ListOptions { Skip = skip, Take = 2 });
            foreach (var t in page)
                Console.WriteLine($"  {t.Name} ({t.VersionCount} version(s), latest v{t.LatestVersionNumber})");
            if (!page.HasMore)
                break;
            skip += page.Count;
        }

        // Free-text search plus a field filter.
        Console.WriteLine("\n--- Search & filters ---");
        var matches = await client.GetTemplatesAsync(new ListOptions
        {
            Search = "invoice",
            Filters = [new Filter("name", FilterOp.Contains, "invoice")],
            SortBy = "name",
            SortDirection = SortDirection.Ascending,
        });
        Console.WriteLine($"  {matches.Total} template(s) matching 'invoice'");

        Console.WriteLine();
        if (await ExampleData.PickPublishedTemplateAsync(client) is not var (template, latest))
            return;

        // A template's content lives on its versions.
        Console.WriteLine($"\n--- Versions of {template.Name} ---");
        var versions = await client.GetTemplateVersionsAsync(
            template.Id, new ListOptions { SortBy = "versionNumber", SortDirection = SortDirection.Descending });
        foreach (var v in versions)
            Console.WriteLine($"  v{v.VersionNumber} — published {v.PublishedAt?.ToString("yyyy-MM-dd") ?? "never"} by {v.PublishedBy ?? "?"}");

        // GetTemplateVersionAsync fetches the latest published version (version: null)
        // or a specific number.
        // SampleData is parsed for you (a JsonElement), so its bindings are directly inspectable.
        var sampleKeys = latest.SampleData.EnumerateObject().Select(p => p.Name).ToList();
        Console.WriteLine($"\nLatest published: v{latest.VersionNumber}, sample data keys: {string.Join(", ", sampleKeys)}");

        // The document-name template controls rendered file names; null clears it.
        var updated = await client.UpdateDocumentNameTemplateAsync(
            template.Id, latest.VersionNumber, "Invoice {{InvoiceNumber}}");
        Console.WriteLine($"Document-name template set to: {updated.DocumentNameTemplate}");
        

        // TODO: Error but can not log in on frontend, so do not know issue
        // Preview image, when the editor has generated one.
        var previewUrl = await client.GetPreviewImageUrlAsync(template.Id, latest.VersionNumber);
        Console.WriteLine($"Preview image: {previewUrl ?? "(none)"}");
    }
}
