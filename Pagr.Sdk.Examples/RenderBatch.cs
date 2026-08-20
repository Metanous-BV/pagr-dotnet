using System.Text.Json;

namespace Pagr.Sdk.Examples;

/// <summary>Synchronous batch render: succeeded/failed items, SaveAllAsync.</summary>
internal static class RenderBatch
{
    public static async Task RunAsync()
    {
        using var client = ExampleEnv.CreateClient();
        if (client is null)
            return;

        if (await ExampleData.PickPublishedTemplateAsync(client) is not var (template, version))
            return;

        // A batch is a list of document data sets — here the sample data three times,
        // plus one deliberately empty document to show failure correlation.
        var inputs = new List<JsonElement>
        {
            version.SampleData,
            version.SampleData,
            version.SampleData,
            JsonSerializer.Deserialize<JsonElement>("{}"),
        };

        var batch = await client.RenderBatchAsync(
            template.Id, inputs, version: version.VersionNumber, includeDocument: true);

        Console.WriteLine(
            $"Batch: {batch.RenderedCount}/{batch.RequestedCount} rendered, ok={batch.Ok}" +
            (batch.InsufficientCredit ? " (insufficient credit!)" : ""));

        // Each BatchItem correlates a submitted input to its outcome, matching the rendered
        // document via the DocumentIndex the API reports for it.
        foreach (var item in batch)
        {
            if (item.Ok)
                Console.WriteLine($"  [{item.Index}] OK — {item.Document!.DocumentName}");
            else
                Console.WriteLine($"  [{item.Index}] FAILED — {string.Join("; ", item.Issues.Select(i => i.Description))}");
        }

        // Write every document that carries inline content in one call.
        var written = await batch.SaveAllAsync(ExampleEnv.OutputDir);
        Console.WriteLine($"Saved {written.Count} PDF(s) to {ExampleEnv.OutputDir}");
    }
}
