using System.Text.Json;
using Pagr.Sdk.Models;

namespace Pagr.Sdk.Examples;

/// <summary>
/// Data validation: severity levels, per-document issues, test vs. production keys.
/// </summary>
/// <remarks>
/// Severity semantics: Information always renders; Warning renders only in test/preview;
/// Error never renders. <c>ValidationResponse.IsValid</c> is the production gate — "no issue
/// of Warning or Error severity". For the narrower, Error-only check, inspect
/// <c>ValidationResponse.Errors</c> directly.
/// </remarks>
internal static class Validate
{
    public static async Task RunAsync()
    {
        using var client = ExampleEnv.CreateClient();
        if (client is null)
            return;

        if (await ExampleData.PickPublishedTemplateAsync(client) is not var (template, version))
            return;

        // Single document: the sample data should be clean.
        Console.WriteLine("\n--- Single document validation ---");
        Print("Base document (should be clean)",
            await client.ValidateAsync(template.Id, version.SampleData, version: version.VersionNumber));

        // An empty document is likely to produce errors.
        Print("Empty document (likely errors)",
            await client.ValidateAsync(template.Id, "{}", version: version.VersionNumber));

        // Batch: issues carry the DocumentIndex they pertain to.
        Console.WriteLine("\n--- Batch validation ---");
        var batch = await client.ValidateAsync(
            template.Id,
            new List<JsonElement> { version.SampleData, JsonSerializer.Deserialize<JsonElement>("{}") },
            version: version.VersionNumber);
        for (var i = 0; i < 2; i++)
        {
            var perDoc = batch.IssuesFor(i);
            var status = perDoc.Any(x => x.IsError) ? "ERROR"
                : perDoc.Any(x => x.Severity == RenderIssueSeverity.Warning) ? "WARNING" : "OK";
            Console.WriteLine($"  doc[{i}] {status} — {perDoc.Count} issue(s)");
        }

        // The same document can validate differently per environment: warnings block
        // production keys but not test keys.
        if (!string.IsNullOrWhiteSpace(ExampleEnv.ProdKey))
        {
            Console.WriteLine("\n--- Test key vs. production key ---");
            client.SetApiKey(ExampleEnv.ProdKey);
            Print("Prod key — validation",
                await client.ValidateAsync(template.Id, version.SampleData, version: version.VersionNumber));

            // The same rule applies to rendering, not just validation: warnings block
            // the production key but not the test key.
            client.SetApiKey(ExampleEnv.TestKey!);
            var testRender = await client.RenderAsync(template.Id, "{}", version: version.VersionNumber);
            Console.WriteLine($"\n  Test key — render empty document: {testRender}");

            client.SetApiKey(ExampleEnv.ProdKey);
            var prodRender = await client.RenderAsync(template.Id, "{}", version: version.VersionNumber);
            Console.WriteLine($"  Prod key — render empty document: {prodRender}");
        }
        else
        {
            Console.WriteLine("\nPROD_KEY_PUBLIC not set — skipping the test-vs-production comparison.");
        }
    }

    private static void Print(string label, ValidationResponse result)
    {
        Console.WriteLine($"\n  {label}");
        if (result.Count == 0)
        {
            Console.WriteLine("    No issues — valid in all environments.");
            return;
        }
        foreach (var issue in result)
            Console.WriteLine($"    {issue}");
        Console.WriteLine($"    renders in test/preview : {result.Errors.Count == 0}  (no errors)");
        Console.WriteLine($"    renders in production   : {result.IsValid}  (no errors and no warnings)");
    }
}
