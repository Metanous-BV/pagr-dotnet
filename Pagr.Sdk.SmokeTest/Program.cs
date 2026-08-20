using Pagr.Sdk;
using Pagr.Sdk.Exceptions;
using Pagr.Sdk.Models;

// Live smoke test. This hits a REAL Pagr API with a REAL API key and consumes credit if
// pointed at a production key — it is not a unit test and is never part of an automated
// run. Run it manually:
//   dotnet run --project Pagr.Sdk.SmokeTest
// It targets the hosted Pagr API by default; set PAGR_BASE_URL to point it at another
// instance. Configuration comes from environment variables, loaded from a .env file found
// by searching upward from the working directory; never hard-code API keys here.
LoadDotEnv();

var baseUrl = Environment.GetEnvironmentVariable("PAGR_BASE_URL");
var apiKey = Environment.GetEnvironmentVariable("PAGR_API_KEY");
var webhookUrl = Environment.GetEnvironmentVariable("PAGR_WEBHOOK_URL");

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("PAGR_API_KEY is not set. Set PAGR_BASE_URL, PAGR_API_KEY and PAGR_WEBHOOK_URL before running.");
    return 1;
}

// The client owns a pooled HttpClient internally — no `new HttpClient()` here.
using var client = new PagrApiClient(apiKey, baseUrl);

try
{
    Console.WriteLine("--- Starting smoke test ---");

    var templatesTask = client.GetTemplatesAsync();
    var statsTask = client.GetOrgStatsAsync();
    var fontsTask = client.GetFontsAsync();
    var statusTask = client.GetStatusAsync();

    async Task RunRenderFlow()
    {
        // No PAGR_TEMPLATE_ID needed — pick the first template in the org that has a
        // published version (same approach as Pagr.Sdk.Examples/ExampleData.cs).
        var page = await client.GetTemplatesAsync(new ListOptions { Take = 50 });
        Guid templateId = default;
        TemplateVersion? latest = null;
        foreach (var template in page)
        {
            try
            {
                latest = await client.GetTemplateVersionAsync(template.Id); // latest published
                templateId = template.Id;
                Console.WriteLine($"Using template: {template.Name} (v{latest.VersionNumber})");
                break;
            }
            catch (PagrNotFoundException)
            {
                // This template has no published version — try the next one.
            }
        }
        if (latest is null)
        {
            Console.WriteLine(
                page.Total == 0
                    ? "No templates in this organisation — skipping render flow."
                    : $"None of the first {page.Count} template(s) has a published version — skipping render flow.");
            return;
        }

        var sampleData = latest.SampleData;

        // Validation (flat issues contract; only Error severity invalidates).
        var validation = await client.ValidateAsync(templateId, sampleData, version: latest.VersionNumber);
        Console.WriteLine(
            $"Validation: isValid={validation.IsValid}, " +
            $"{validation.Errors.Count} error(s), {validation.Warnings.Count} warning(s)");

        // Single render (full document, not a hand-copied subset).
        var renderResult = await client.RenderAsync(
            templateId, sampleData, version: latest.VersionNumber, includeDocument: true);
        if (renderResult.Ok)
        {
            Console.WriteLine($"Render OK — ViewUrl: {renderResult.Document!.ViewUrl}");
            var saved = await renderResult.Document.SaveAsync(AppContext.BaseDirectory);
            Console.WriteLine($"Saved rendered PDF to: {saved}");
        }
        else
        {
            Console.WriteLine($"Render failed: {string.Join("; ", renderResult.Issues)}");
        }
        if (renderResult.InsufficientCredit)
            Console.WriteLine("Warning: insufficient credit reported.");

        // Batch render
        var repeated = Enumerable.Repeat(sampleData, 20).ToList();
        var batch = await client.RenderBatchAsync(
            templateId, repeated, version: latest.VersionNumber, includeDocument: true);
        Console.WriteLine($"Batch: rendered={batch.RenderedCount}, requested={batch.RequestedCount}, ok={batch.Ok}");
        foreach (var item in batch.Succeeded)
            Console.WriteLine($"  [{item.Index}] {item.Document!.DocumentName} — {item.Document.ViewUrl}");

        // Async webhook job + polling (skipped when no webhook URL is configured)
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            var job = await client.EnqueueBatchRenderAsync(
                templateId, repeated, webhookUrl, version: latest.VersionNumber);
            Console.WriteLine($"Enqueued async job {job.JobId} (state={job.State}).");

            var jobStatus = await client.GetJobStatusAsync(job.JobId);
            Console.WriteLine($"Job status: state={jobStatus.State} status={jobStatus.Status} ({jobStatus.RenderedCount} rendered, done={jobStatus.Done})");
        }
    }

    await Task.WhenAll(templatesTask, statsTask, fontsTask, statusTask, RunRenderFlow());

    Console.WriteLine($"\nAPI healthy: {statusTask.Result}");
    Console.WriteLine($"Templates: {templatesTask.Result.Total}");
    Console.WriteLine($"Fonts: {fontsTask.Result.Count}");
    Console.WriteLine($"Org pages available: {statsTask.Result.PagesAvailable}, tokens available: {statsTask.Result.TokensAvailable}");
    Console.WriteLine("\nSmoke test completed successfully!");
    return 0;
}
catch (PagrApiException ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Pagr API error (HTTP {ex.StatusCode}, code {ex.Code}): {ex.Message}");
    Console.ResetColor();
    return 1;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"An error occurred: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    Console.ResetColor();
    return 1;
}

// Loads a .env file (if found) into the process environment. Searches upward from both
// the executable's directory and the current working directory; existing process env
// vars always win. Mirrors Pagr.Sdk.Examples/ExampleEnv.cs.
static void LoadDotEnv()
{
    foreach (var dir in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        var envFile = FindUpwards(dir, ".env");
        if (envFile is null)
            continue;
        foreach (var line in File.ReadAllLines(envFile))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || !trimmed.Contains('='))
                continue;
            var key = trimmed[..trimmed.IndexOf('=')].Trim();
            var value = trimmed[(trimmed.IndexOf('=') + 1)..].Trim().Trim('"');
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
        break;
    }
}

static string? FindUpwards(string start, string fileName)
{
    for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, fileName);
        if (File.Exists(candidate))
            return candidate;
    }
    return null;
}
