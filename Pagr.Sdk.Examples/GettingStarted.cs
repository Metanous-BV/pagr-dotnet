namespace Pagr.Sdk.Examples;

/// <summary>Your first render: connect, pick a template, render a document, save the PDF.</summary>
internal static class GettingStarted
{
    public static async Task RunAsync()
    {
        using var client = ExampleEnv.CreateClient();
        if (client is null)
            return;

        // Check the service is reachable before doing any work.
        await client.GetStatusAsync();
        Console.WriteLine($"Connected to Pagr API {await client.GetVersionAsync()} at {ExampleEnv.BaseUrl ?? PagrApiClient.DefaultBaseUrl}");

        // Pick a template with a published version, and that latest published version.
        if (await ExampleData.PickPublishedTemplateAsync(client) is not var (template, version))
            return;

        // Every version carries sample data matching its bindings — a good starting
        // point for your own document. It is a JSON string, ready to pass to RenderAsync.
        var result = await client.RenderAsync(
            template.Id, version.SampleData, version: version.VersionNumber, includeDocument: true);
        if (!result.Ok)
        {
            Console.WriteLine($"Render failed: {result.Message ?? result.Status}");
            foreach (var issue in result.Issues)
                Console.WriteLine($"  [{issue.Severity}] {issue.Description}");
            return;
        }

        var document = result.Document!;
        Console.WriteLine(
            $"Rendered {document.DocumentName}: {document.PageCount} page(s), {document.FileSizeBytes} bytes");
        Console.WriteLine($"Saved to {await document.SaveAsync(ExampleEnv.OutputDir)}");
    }
}
