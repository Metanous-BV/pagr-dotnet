namespace Pagr.Sdk.Examples;

/// <summary>Single render options: issues, language override, persist=false, JSON-string data.</summary>
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

        // Multilingual templates render a specific variant via `language`.
        var dutch = await client.RenderAsync(
            template.Id, version.SampleData, version: version.VersionNumber,
            includeDocument: true, language: "nl");
        Console.WriteLine($"Language override render ok: {dutch.Ok}");

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
