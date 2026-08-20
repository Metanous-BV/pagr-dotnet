namespace Pagr.Sdk.Examples;

/// <summary>Listing and downloading previously rendered documents.</summary>
internal static class Documents
{
    public static async Task RunAsync()
    {
        using var client = ExampleEnv.CreateClient();
        if (client is null)
            return;

        // Newest first, five per page.
        var page = await client.GetDocumentsAsync(new ListOptions
        {
            Take = 5,
            SortBy = "renderedAt",
            SortDirection = SortDirection.Descending,
        });
        Console.WriteLine($"{page.Total} rendered document(s) in this organisation; showing {page.Count}:");
        foreach (var doc in page)
        {
            //Local time, cause stored in UTC
            Console.WriteLine(
                $"  {doc.DocumentName} — {doc.PageCount} page(s), {doc.FileSizeBytes / 1024.0:F1} KB, " +
                $"rendered {doc.RenderedAt.ToLocalTime():yyyy-MM-dd HH:mm} ({doc.Environment})" +
                (doc.IsPdfDeleted ? " [PDF deleted]" : ""));
        }

        var target = page.FirstOrDefault(d => !d.IsPdfDeleted);
        if (target is null)
        {
            Console.WriteLine("No downloadable documents — render something first.");
            return;
        }

        // Metadata by id, then the stored PDF bytes.
        var meta = await client.GetDocumentAsync(target.Id);
        Console.WriteLine($"\nDownloading {meta.DocumentName} (template {meta.TemplateId}, v{meta.VersionNumber})");

        var bytes = await client.DownloadDocumentAsync(target.Id);
        var path = Path.Combine(ExampleEnv.OutputDir, $"{meta.DocumentName}.pdf");
        await File.WriteAllBytesAsync(path, bytes);
        Console.WriteLine($"Saved {bytes.Length} bytes to {path}");
    }
}
