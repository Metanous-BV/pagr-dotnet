using Pagr.Sdk.Exceptions;

namespace Pagr.Sdk.Examples;

/// <summary>The exception hierarchy and when to catch what.</summary>
internal static class ErrorHandling
{
    public static async Task RunAsync()
    {
        // Transport and API failures throw PagrApiException subclasses. Business outcomes
        // (failed validation, insufficient credit, per-document render failures) are DATA
        // on the result objects — they never throw.

        // 401: a bad key throws the most specific type first.
        using (var badClient = new PagrApiClient("not-a-real-key", ExampleEnv.BaseUrl))
        {
            try
            {
                await badClient.GetTemplatesAsync();
            }
            catch (PagrAuthenticationException ex)
            {
                Console.WriteLine($"Caught PagrAuthenticationException (HTTP {ex.StatusCode}, code {ex.Code}): {ex.Message}");
            }
        }

        using var client = ExampleEnv.CreateClient();
        if (client is null)
            return;
        if (!string.IsNullOrWhiteSpace(ExampleEnv.ProdKey))
            client.SetApiKey(ExampleEnv.ProdKey);

        // 404: unknown ids map to PagrNotFoundException.
        try
        {
            await client.GetTemplateAsync(Guid.NewGuid());
        }
        catch (PagrNotFoundException ex)
        {
            Console.WriteLine($"Caught PagrNotFoundException: {ex.Message}");
        }

        // Catch the base type when any API failure should be handled the same way.
        // Order matters: catch subclasses before PagrApiException.
        try
        {
            await client.GetDocumentAsync(Guid.NewGuid());
        }
        catch (PagrNotFoundException)
        {
            Console.WriteLine("Specific handling: the document does not exist.");
        }
        catch (PagrApiException ex)
        {
            Console.WriteLine($"Generic handling for anything else: HTTP {ex.StatusCode}");
        }

        // Business outcome, not an exception: a failed render reports issues as data.
        if (await ExampleData.PickPublishedTemplateAsync(client) is var (template, _))
        {
            var result = await client.RenderAsync(template.Id, "{}");
            Console.WriteLine($"\nRender of an empty document: Ok={result.Ok} (no exception thrown)");
            foreach (var issue in result.Issues)
                Console.WriteLine($"  [{issue.Severity}] {issue.Type}: {issue.Description}");
            if (result.InsufficientCredit)
                Console.WriteLine("  Insufficient credit — also data, not an exception.");
        }
    }
}
