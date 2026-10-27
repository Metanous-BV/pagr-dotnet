namespace Pagr.Sdk.Examples;

/// <summary>Picking a project, creating a template with content, editing a draft version, setting its default language.</summary>
internal static class TemplateAuthoring
{
    public static async Task RunAsync()
    {
        using var client = ExampleEnv.CreateClient();
        if (client is null)
            return;

        // New templates live in a project. Every organisation has at least one; pick the first by name.
        var projects = await client.GetProjectsAsync(new ListOptions { SortBy = "name", Take = 1 });
        var projectId = projects[0].Id;
        Console.WriteLine($"Using project: {projects[0]} ({projects.Total} project(s) in total)");

        // template, data and translations go over the wire as JSON objects: pass an anonymous
        // object, a dictionary, a JsonElement/JsonNode, or a JSON string. Left out, each gets
        // the starter content. Version 1 is created as a draft.
        var created = await client.CreateTemplateAsync(
            projectId, $"SDK example {DateTime.UtcNow:yyyyMMdd-HHmmss}",
            data: new { invoiceNumber = "2026-001" },
            translations: new { fr = new { title = "Facture" } },
            defaultLanguage: "nl");
        Console.WriteLine($"Created: {created}");

        // Only the arguments you set are sent; everything else keeps its stored value.
        var v1 = await client.UpdateTemplateVersionAsync(
            created.Id, 1,
            translations: new { fr = new { title = "Facture" }, en = new { title = "Invoice" } });
        Console.WriteLine($"v1 can render in: {string.Join(", ", v1.AvailableLanguages)}");

        // The API canonicalises the culture name; read the stored value back from the result.
        v1 = await client.UpdateDefaultLanguageAsync(created.Id, 1, " NL-be ");
        Console.WriteLine($"Default language stored as: {v1.DefaultLanguage}");

        // There is no publish endpoint: publish the draft in the Pagr editor.
    }
}
