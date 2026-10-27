namespace Pagr.Sdk.Examples;

/// <summary>Single render options: issues, picking a language, persist=false, JSON-string data.</summary>
internal static class RenderSingle
{
    public static async Task RunAsync()
    {
        using var client = ExampleEnv.CreateClient();
        if (client is null)
            return;

        if (await ExampleData.PickPublishedTemplateAsync(client) is not var (template, version))
            return;

        // Data can be a JsonElement (here: the version's parsed sample data), a JSON string,
        // or any serialisable object — see the RenderAsync overloads.
        var result = await client.RenderAsync(
            template.Id, version.SampleData, version: version.VersionNumber, includeDocument: true);
        if (result.Ok)
            Console.WriteLine($"RendGiveered: {await result.Document!.SaveAsync(ExampleEnv.OutputDir)}");

        // Issues explain anything that went wrong (or degraded) during the render.
        foreach (var issue in result.Issues)
            Console.WriteLine($"  [{issue.Severity}] {issue.Type}: {issue.Description}");

        // Without `language` a render uses the version's default language. AvailableLanguages
        // lists every valid value (default first, then the translations), so pick from it.
        Console.WriteLine(
            $"Default language: {version.DefaultLanguage ?? "(unknown)"}; " +
            $"available: {(version.AvailableLanguages.Count > 0 ? string.Join(", ", version.AvailableLanguages) : "(none)")}");
        var translated = version.AvailableLanguages.FirstOrDefault(l => l != version.DefaultLanguage);
        if (translated is not null)
        {
            var other = await client.RenderAsync(
                template.Id, version.SampleData, version: version.VersionNumber,
                includeDocument: true, language: translated);
            Console.WriteLine($"Language override render ok: {other.Ok} (rendered in {other.Document?.Language})");
        }

        // persist: false skips server-side storage; Id and ViewUrl come back null.
        var unstored = await client.RenderAsync(
            template.Id, version.SampleData, version: version.VersionNumber,
            includeDocument: true, persist: false);
        if (unstored.Ok)
            Console.WriteLine($"Non-persisted render: {unstored.Document!.ToBytes().Length} bytes (not stored server-side)");

        // RenderPdfAsync streams the raw PDF instead of a Base64 field, with the metadata in
        // X-Pagr-* response headers. A blocked render is data (Ok == false), never an exception.
        var pdfResult = await client.RenderPdfAsync(
            template.Id, version.SampleData, version: version.VersionNumber);
        if (pdfResult.Ok)
            Console.WriteLine($"Raw-PDF render: {await pdfResult.Document!.SaveAsync(ExampleEnv.OutputDir)}");
        else
            Console.WriteLine($"Raw-PDF render failed ({pdfResult.Status}): {string.Join("; ", pdfResult.Issues)}");
    }
}
