using Pagr.Sdk.Examples;

// Runnable examples, one topic each — pick one by name:
//   dotnet run --project Pagr.Sdk.Examples -- getting-started
var examples = new Dictionary<string, (string Description, Func<Task> Run)>(StringComparer.OrdinalIgnoreCase)
{
    ["getting-started"] = ("Connect, pick a template, render a document, save the PDF.", GettingStarted.RunAsync),
    ["templates"] = ("Browse templates and versions: paging, search, filters, document-name template, preview image.", Templates.RunAsync),
    ["render-single"] = ("Single render options: issues, language override, persist=false, JSON-string data.", RenderSingle.RunAsync),
    ["render-batch"] = ("Synchronous batch render: succeeded/failed items, SaveAllAsync.", RenderBatch.RunAsync),
    ["batch-async"] = ("Fire-and-forget batch render with webhook callbacks, plus job status polling.", BatchAsync.RunAsync),
    ["validate"] = ("Data validation: severity levels, per-document issues, test vs. production keys.", Validate.RunAsync),
    ["documents"] = ("Listing and downloading previously rendered documents.", Documents.RunAsync),
    ["account"] = ("Organisation usage stats, available fonts, API key rotation.", Account.RunAsync),
    ["error-handling"] = ("The exception hierarchy and when to catch what.", ErrorHandling.RunAsync),
};

if (args.Length != 1 || !examples.TryGetValue(args[0], out var example))
{
    Console.WriteLine("Usage: dotnet run --project Pagr.Sdk.Examples -- <example>\n\nAvailable examples:");
    foreach (var (name, (description, _)) in examples)
        Console.WriteLine($"  {name,-16} {description}");
    return 1;
}

try
{
    await example.Run();
    return 0;
}
catch (Pagr.Sdk.Exceptions.PagrApiException ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"\nPagr API error (HTTP {ex.StatusCode}, code {ex.Code}): {ex.Message}");
    Console.ResetColor();
    return 1;
}
