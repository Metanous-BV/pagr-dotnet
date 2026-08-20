using System.Text;
using System.Text.Json;
using Pagr.Sdk.Exceptions;
using Xunit;

namespace Pagr.Sdk.Tests;

public class ClientTests
{
    private static readonly Guid TemplateId = Guid.Parse(TestFixtures.TemplateId);

    private static PagrApiClient NewClient(StubHttpMessageHandler handler) =>
        new(new PagrClientOptions { BaseUrl = "http://localhost", ApiKey = "key" }, handler);

    // ── Construction ─────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_BaseUrl_DefaultsToHostedApi()
    {
        // baseUrl is optional; omitting it targets the hosted Pagr API.
        using var client = new PagrApiClient("key");
        Assert.Equal(PagrApiClient.DefaultBaseUrl, client.Transport.BaseUrl);
    }

    [Fact]
    public void Constructor_BaseUrl_ExplicitValueOverridesDefault()
    {
        // Explicit baseUrl wins and the trailing slash is normalised away.
        using var client = new PagrApiClient("key", "https://api.example.com/");
        Assert.Equal("https://api.example.com", client.Transport.BaseUrl);
    }

    // ── Render ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderBatch_CorrelatesInputsAndIssues()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, $$"""
            {
              "status": "ok",
              "renderedCount": 2,
              "requestedCount": 3,
              "missingCount": 1,
              "documents": [
                {{TestFixtures.MakeDocNode("Doc 0", documentIndex: 0).ToJsonString()}},
                {{TestFixtures.MakeDocNode("Doc 2", documentIndex: 2).ToJsonString()}}
              ],
              "issues": [
                {{TestFixtures.MakeIssueNode("Error", "SchemaInvalid", "bad field", documentIndex: 1).ToJsonString()}}
              ]
            }
            """);

        using var client = NewClient(handler);
        var inputs = new[]
        {
            new { Title = "Doc 0" },
            new { Title = "Doc 1" },
            new { Title = "Doc 2" },
        };

        var result = await client.RenderBatchAsync(TemplateId, inputs, version: 1);

        Assert.Equal($"/v1/render/{TemplateId}/versions/1", handler.LastPath);
        Assert.Equal(3, result.Count);
        Assert.False(result.Ok);

        // Failed item carries its exact input + typed issue.
        Assert.False(result[1].Ok);
        var issue = Assert.Single(result[1].Issues);
        Assert.Equal("bad field", issue.Description);
        Assert.Equal(Models.RenderIssueType.SchemaInvalid, issue.Type);
        Assert.Equal("Doc 1", result[1].Input!.Value.GetProperty("Title").GetString());

        // Successful docs land on the non-failed slots, in order.
        Assert.Equal("Doc 0", result[0].Document!.DocumentName);
        Assert.Equal("Doc 2", result[2].Document!.DocumentName);
        Assert.Equal([1], result.Failed.Select(it => it.Index));
    }

    [Fact]
    public async Task Render_Latest_UsesVersionlessPath()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, $$"""
            {"status": "ok", "renderedCount": 1, "requestedCount": 1, "missingCount": 0,
             "documents": [{{TestFixtures.MakeDocNode("Doc 0").ToJsonString()}}]}
            """);

        using var client = NewClient(handler);
        var result = await client.RenderAsync(TemplateId, """{"Title": "x"}""");

        Assert.Equal($"/v1/render/{TemplateId}", handler.LastPath);
        Assert.True(result.Ok);
    }

    [Fact]
    public async Task Render_PersistFalse_ReturnsJsonEnvelopeWithNullIds()
    {
        // persist=false does not stream a raw PDF: it returns the ordinary JSON envelope with
        // id/viewUrl null and the base64 field populated. The SDK parses it as normal JSON —
        // there is no content-type sniffing on this path (only RenderPdfAsync asks for a PDF).
        var docNode = TestFixtures.MakeDocNode("Doc 0", Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-1.7")));
        docNode["id"] = null;
        docNode["viewUrl"] = null;
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, $$"""
            {"status": "ok", "renderedCount": 1, "requestedCount": 1, "missingCount": 0,
             "documents": [{{docNode.ToJsonString()}}], "issues": []}
            """);

        using var client = NewClient(handler);
        var result = await client.RenderAsync(
            TemplateId, """{"Title": "x"}""", includeDocument: true, persist: false);

        Assert.Contains("persist=false", handler.LastQuery);
        Assert.True(result.Ok);
        Assert.Equal(1, result.RenderedCount);
        Assert.Null(result.Document!.Id);
        Assert.Null(result.Document.ViewUrl);
        Assert.Equal(Encoding.UTF8.GetBytes("%PDF-1.7"), result.Document.ToBytes());
    }

    // ── Render (raw PDF) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderPdf_StreamsBytes_WithHeaderMetadata()
    {
        var pdf = Encoding.UTF8.GetBytes("%PDF-1.7 real");
        var handler = new StubHttpMessageHandler();
        handler.RespondBytes(200, pdf, "application/pdf", new Dictionary<string, string>
        {
            ["Content-Disposition"] = "attachment; filename=\"Doc 0.pdf\"",
            ["X-Pagr-Document-Id"] = "550e8400-e29b-41d4-a716-446655440000",
            ["X-Pagr-Page-Count"] = "2",
            ["X-Pagr-Render-Duration-Ms"] = "99.5",
            ["X-Pagr-View-Url"] = "https://example.test/doc",
            ["X-Pagr-Issue-Count"] = "1",
        });

        using var client = NewClient(handler);
        var result = await client.RenderPdfAsync(TemplateId, new { Title = "a" });

        Assert.Equal($"/v1/render/{TemplateId}", handler.LastPath);
        // The Accept header is what opts into the raw-PDF path.
        Assert.Equal("application/pdf", handler.LastAccept);
        Assert.Equal("?persist=true", handler.LastQuery);

        Assert.True(result.Ok);
        Assert.Equal("ok", result.Status);
        var document = result.Document!;
        Assert.Equal("Doc 0", document.DocumentName);   // ".pdf" stripped
        Assert.Equal(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"), document.DocumentId);
        Assert.Equal(2, document.PageCount);
        Assert.Equal(99.5, document.RenderDuration);
        Assert.Equal("https://example.test/doc", document.ViewUrl);
        Assert.Equal(1, document.IssueCount);
        Assert.Equal(pdf, document.ToBytes());
    }

    [Fact]
    public async Task RenderPdf_SendsSingleElementDocumentsArray_WithoutIncludeDocument()
    {
        var handler = new StubHttpMessageHandler();
        handler.RespondBytes(200, [1, 2, 3], "application/pdf");

        using var client = NewClient(handler);
        await client.RenderPdfAsync(TemplateId, """{"Title": "x"}""", version: 4, language: "nl", persist: false);

        Assert.Equal($"/v1/render/{TemplateId}/versions/4", handler.LastPath);
        Assert.Equal("?language=nl&persist=false", handler.LastQuery);

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        var documents = body.GetProperty("documents");
        Assert.Equal(1, documents.GetArrayLength());
        Assert.Equal("x", documents[0].GetProperty("Title").GetString());
        // includeDocument only applies to the JSON-envelope path.
        Assert.False(body.TryGetProperty("includeDocument", out _));
    }

    [Fact]
    public async Task RenderPdf_MissingHeaders_LeaveNullsNotPlaceholders()
    {
        var handler = new StubHttpMessageHandler();
        handler.RespondBytes(200, Encoding.UTF8.GetBytes("%PDF"), "application/pdf");

        using var client = NewClient(handler);
        var result = await client.RenderPdfAsync(TemplateId, new { Title = "a" }, persist: false);

        var document = result.Document!;
        Assert.Null(document.DocumentId);   // never Guid.Empty
        Assert.Null(document.ViewUrl);      // never ""
        Assert.Equal("document", document.DocumentName);
        Assert.Equal(0, document.PageCount);
        Assert.Equal(0.0, document.RenderDuration);
        Assert.Equal(0, document.IssueCount);
    }

    [Fact]
    public async Task RenderPdf_422_IsBusinessOutcomeNotException()
    {
        // A blocked render has no PDF to stream → 422 with the JSON envelope. The SDK returns
        // it as data (PdfRenderResult.Ok is false) and never raises.
        var handler = new StubHttpMessageHandler();
        handler.Respond(422, $$"""
            {"status": "failed", "message": "blocked by content sanitizer",
             "issues": [{{TestFixtures.MakeIssueNode("Error", "DangerousContent", "script tag", documentIndex: 0).ToJsonString()}}]}
            """);

        using var client = NewClient(handler);
        var result = await client.RenderPdfAsync(TemplateId, new { Title = "a" });

        Assert.False(result.Ok);
        Assert.Equal("failed", result.Status);
        Assert.Null(result.Document);
        Assert.Equal("blocked by content sanitizer", result.Message);
        Assert.Equal(["script tag"], result.Issues.Select(i => i.Description));
    }

    [Fact]
    public async Task RenderPdf_InsufficientCredit_IsDataNotException()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(422, """{"status": "insufficient_credit", "message": "out of credit"}""");

        using var client = NewClient(handler);
        var result = await client.RenderPdfAsync(TemplateId, new { Title = "a" });

        Assert.True(result.InsufficientCredit);
        Assert.False(result.Ok);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task RenderPdf_OtherErrorStatuses_StillThrow()
    {
        // Only 422 is non-raising: everything else maps to the usual typed exception.
        var handler = new StubHttpMessageHandler();
        handler.Respond(404, """{"error": {"code": "NotFound", "message": "no such template"}}""");

        using var client = NewClient(handler);
        await Assert.ThrowsAsync<PagrNotFoundException>(
            () => client.RenderPdfAsync(TemplateId, new { Title = "a" }));
    }

    [Fact]
    public async Task RenderPdf_PayloadOverloads_ProduceIdenticalBodies()
    {
        var handler = new StubHttpMessageHandler();
        handler.RespondBytes(200, [1], "application/pdf");
        using var client = NewClient(handler);

        await client.RenderPdfAsync(TemplateId, """{"Title":"x"}""");
        var fromString = handler.LastRequestBody;

        await client.RenderPdfAsync(TemplateId, JsonSerializer.Deserialize<JsonElement>("""{"Title":"x"}"""));
        var fromElement = handler.LastRequestBody;

        await client.RenderPdfAsync(TemplateId, new { Title = "x" });
        var fromPoco = handler.LastRequestBody;

        Assert.Equal(fromString, fromElement);
        Assert.Equal(fromString, fromPoco);
    }

    [Fact]
    public async Task Render_SendsPersistAndLanguageQuery()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"status": "ok", "documents": []}""");

        using var client = NewClient(handler);
        await client.RenderAsync(TemplateId, """{}""", language: "nl");
        Assert.Equal("?language=nl&persist=true", handler.LastQuery);

        await client.RenderAsync(TemplateId, """{}""");
        Assert.Equal("?persist=true", handler.LastQuery);
    }

    [Fact]
    public async Task Render_InsufficientCredit_IsDataNotException()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """
            {
              "status": "insufficient_credit",
              "message": "out of credit",
              "renderedCount": 0,
              "requestedCount": 1,
              "missingCount": 1,
              "documents": []
            }
            """);

        using var client = NewClient(handler);
        var result = await client.RenderAsync(TemplateId, new { Title = "x" }, version: 1);

        Assert.True(result.InsufficientCredit);
        Assert.False(result.Ok);
        Assert.Null(result.Document);
        Assert.Equal("out of credit", result.Message);
    }

    [Fact]
    public async Task PayloadOverloads_ProduceIdenticalBodies()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"status": "ok", "documents": []}""");
        using var client = NewClient(handler);

        await client.RenderAsync(TemplateId, """{"Title":"x"}""", version: 1);
        var fromString = handler.LastRequestBody;

        await client.RenderAsync(TemplateId, JsonSerializer.Deserialize<JsonElement>("""{"Title":"x"}"""), version: 1);
        var fromElement = handler.LastRequestBody;

        await client.RenderAsync(TemplateId, new { Title = "x" }, version: 1);
        var fromPoco = handler.LastRequestBody;

        Assert.Equal(fromString, fromElement);
        Assert.Equal(fromString, fromPoco);
        Assert.Contains("\"Title\":\"x\"", fromString);
    }

    // ── Async jobs ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueBatchRender_ReturnsTypedJob()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(202, """
            {"jobId": "f47ac10b-58cc-4372-a567-0e02b2c3d479", "requestedCount": 3, "state": "queued"}
            """);

        using var client = NewClient(handler);
        var job = await client.EnqueueBatchRenderAsync(
            TemplateId, new[] { new { Title = "a" } }, "http://localhost/cb", version: 1);

        Assert.Equal($"/v1/render/{TemplateId}/versions/1/async", handler.LastPath);
        Assert.Equal(3, job.RequestedCount);
        Assert.Equal(Models.RenderJobState.Queued, job.State);
        Assert.Equal("f47ac10b-58cc-4372-a567-0e02b2c3d479", job.JobId.ToString());

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        Assert.Equal("http://localhost/cb", body.GetProperty("callbackUrl").GetString());
    }

    [Fact]
    public async Task EnqueueBatchRender_Latest_UsesVersionlessAsyncPath()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(202, """
            {"jobId": "f47ac10b-58cc-4372-a567-0e02b2c3d479", "requestedCount": 1, "state": "queued"}
            """);

        using var client = NewClient(handler);
        await client.EnqueueBatchRenderAsync(TemplateId, new[] { """{}""" }, "http://localhost/cb");

        Assert.Equal($"/v1/render/{TemplateId}/async", handler.LastPath);
    }

    [Fact]
    public async Task GetJobStatus_ReturnsTypedStatus()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """
            {"jobId": "f47ac10b-58cc-4372-a567-0e02b2c3d479", "state": "completed", "status": "ok",
             "renderedCount": 3, "requestedCount": 3, "missingCount": 0, "startedAt": "2026-07-16T10:00:00Z",
             "completedAt": "2026-07-16T10:00:05Z"}
            """);

        using var client = NewClient(handler);
        var jobId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
        var status = await client.GetJobStatusAsync(jobId);

        Assert.Equal($"/v1/render/jobs/{jobId}", handler.LastPath);
        Assert.True(status.Done);
        Assert.True(status.Ok);
        Assert.Equal(3, status.RenderedCount);
        Assert.NotNull(status.CompletedAt);
    }

    [Fact]
    public async Task WaitForJob_PollsUntilTerminal()
    {
        var handler = new StubHttpMessageHandler();
        var jobId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
        var pending = $$"""{"jobId": "{{jobId}}", "state": "pending", "renderedCount": 0, "requestedCount": 1, "missingCount": 0, "startedAt": "2026-07-16T10:00:00Z"}""";
        handler.EnqueueResponse(200, pending);
        handler.EnqueueResponse(200, pending);
        handler.Respond(200, $$"""{"jobId": "{{jobId}}", "state": "completed", "status": "ok", "renderedCount": 1, "requestedCount": 1, "missingCount": 0, "startedAt": "2026-07-16T10:00:00Z"}""");

        using var client = NewClient(handler);
        var status = await client.WaitForJobAsync(jobId, pollInterval: TimeSpan.FromMilliseconds(1));

        Assert.True(status.Done);
        Assert.Equal(Models.RenderJobState.Completed, status.State);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task WaitForJob_UnknownState_IsTerminal()
    {
        var handler = new StubHttpMessageHandler();
        var jobId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
        handler.Respond(200, $$"""
            {"jobId": "{{jobId}}", "state": "cancelled", "renderedCount": 0, "requestedCount": 1,
             "missingCount": 0, "startedAt": "2026-07-16T10:00:00Z"}
            """);

        using var client = NewClient(handler);
        var status = await client.WaitForJobAsync(jobId, pollInterval: TimeSpan.FromMilliseconds(1));

        Assert.True(status.Done);
        Assert.Equal(Models.RenderJobState.Unknown, status.State);
    }

    [Fact]
    public async Task WaitForJob_TimesOut()
    {
        var handler = new StubHttpMessageHandler();
        var jobId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
        handler.Respond(200, $$"""
            {"jobId": "{{jobId}}", "state": "pending", "renderedCount": 0, "requestedCount": 1,
             "missingCount": 0, "startedAt": "2026-07-16T10:00:00Z"}
            """);

        using var client = NewClient(handler);
        await Assert.ThrowsAsync<PagrTimeoutException>(() => client.WaitForJobAsync(
            jobId, pollInterval: TimeSpan.FromMilliseconds(5), timeout: TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public async Task WaitForJob_NoTimeoutPassed_UsesBoundedDefault_NotUnbounded()
    {
        // A job that never reaches a terminal state must not hang the caller forever when the
        // caller omits `timeout` entirely. Override the default (5 minutes in production) down
        // to a few milliseconds so the test doesn't actually wait 5 minutes, but otherwise this
        // exercises the exact "no timeout argument supplied" path a caller would hit.
        var handler = new StubHttpMessageHandler();
        var jobId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
        handler.Respond(200, $$"""
            {"jobId": "{{jobId}}", "state": "pending", "renderedCount": 0, "requestedCount": 1,
             "missingCount": 0, "startedAt": "2026-07-16T10:00:00Z"}
            """);

        using var client = NewClient(handler);
        client.WaitForJobDefaultTimeoutOverride = TimeSpan.FromMilliseconds(20);

        await Assert.ThrowsAsync<PagrTimeoutException>(() => client.WaitForJobAsync(
            jobId, pollInterval: TimeSpan.FromMilliseconds(5)));
    }

    [Fact]
    public async Task WaitForJob_InfiniteTimeoutRequested_PollsUntilTerminalWithoutDeadline()
    {
        // Timeout.InfiniteTimeSpan is the explicit opt-out into unbounded polling; it must
        // never trip the (overridden, short) default deadline.
        var handler = new StubHttpMessageHandler();
        var jobId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
        var pending = $$"""{"jobId": "{{jobId}}", "state": "pending", "renderedCount": 0, "requestedCount": 1, "missingCount": 0, "startedAt": "2026-07-16T10:00:00Z"}""";
        handler.EnqueueResponse(200, pending);
        handler.EnqueueResponse(200, pending);
        handler.Respond(200, $$"""{"jobId": "{{jobId}}", "state": "completed", "status": "ok", "renderedCount": 1, "requestedCount": 1, "missingCount": 0, "startedAt": "2026-07-16T10:00:00Z"}""");

        using var client = NewClient(handler);
        client.WaitForJobDefaultTimeoutOverride = TimeSpan.FromMilliseconds(1);

        var status = await client.WaitForJobAsync(
            jobId, pollInterval: TimeSpan.FromMilliseconds(1), timeout: Timeout.InfiniteTimeSpan);

        Assert.True(status.Done);
        Assert.Equal(Models.RenderJobState.Completed, status.State);
    }

    // ── Templates ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTemplates_ReturnsPagedResult()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, $$"""
            {"items": [{"id": "{{TestFixtures.TemplateId}}", "name": "Invoice"}],
             "total": 5, "skip": 0, "take": 1}
            """);

        using var client = NewClient(handler);
        var page = await client.GetTemplatesAsync();

        Assert.Equal("/v1/templates", handler.LastPath);
        Assert.Single(page.Items);
        Assert.Equal(5, page.Total);
        Assert.True(page.HasMore);
        Assert.Equal("Invoice", page[0].Name);
        Assert.Equal(Guid.Parse(TestFixtures.TemplateId), page[0].Id);
    }

    [Fact]
    public async Task GetTemplates_WithProjectId_UsesProjectPath()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"items": [], "total": 0, "skip": 0, "take": 0}""");

        using var client = NewClient(handler);
        var projectId = Guid.NewGuid();
        await client.GetTemplatesAsync(projectId);

        Assert.Equal($"/v1/projects/{projectId}/templates", handler.LastPath);
    }

    [Fact]
    public async Task ListOptions_SerializeToIndexedFilterQuery()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"items": [], "total": 0, "skip": 0, "take": 0}""");

        using var client = NewClient(handler);
        await client.GetTemplatesAsync(new ListOptions
        {
            Skip = 5,
            Take = 10,
            SortBy = "name",
            SortDirection = SortDirection.Descending,
            Search = "invoice",
            Filters =
            [
                // "project.guid" (not "projectName") is the only field the canonical
                // TEMPLATE_FILTERS table allows for project filtering.
                new Filter("project.guid", "Sales"),
                new Filter("name", FilterOp.Contains, "inv"),
            ],
        });

        Assert.Equal(
            "?skip=5&take=10&sortBy=name&sortDirection=desc&search=invoice" +
            "&filters[0].field=project.guid&filters[0].op=eq&filters[0].value=Sales" +
            "&filters[1].field=name&filters[1].op=contains&filters[1].value=inv",
            handler.LastQuery);
    }

    [Fact]
    public async Task GetTemplates_UnknownFilterField_Throws()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"items": [], "total": 0, "skip": 0, "take": 0}""");
        using var client = NewClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetTemplatesAsync(
            new ListOptions { Filters = [new Filter("projectName", "Sales")] }));
    }

    [Fact]
    public async Task GetDocuments_UnknownFilterOperator_Throws()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"items": [], "total": 0, "skip": 0, "take": 0}""");
        using var client = NewClient(handler);

        // "contains" is not valid for the numeric pageCount field.
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetDocumentsAsync(
            new ListOptions { Filters = [new Filter("pageCount", FilterOp.Contains, "3")] }));
    }

    [Fact]
    public async Task GetDocuments_FilterValue_GuidAndDateTimeCoerced()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"items": [], "total": 0, "skip": 0, "take": 0}""");
        using var client = NewClient(handler);

        var templateGuid = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        var renderedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await client.GetDocumentsAsync(new ListOptions
        {
            Filters =
            [
                Filter.Eq("template.guid", templateGuid),
                Filter.Gte("renderedAt", renderedAt),
            ],
        });

        Assert.Contains($"filters[0].value={templateGuid}", handler.LastQuery);
        Assert.Contains("filters[1].value=2026-01-01T00:00:00+00:00", handler.LastQuery);
    }

    [Fact]
    public async Task GetTemplateVersion_DefaultsToLatest()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, $$"""
            {"id": "550e8400-e29b-41d4-a716-446655440000", "versionNumber": 7,
             "templateJson": "{}", "sampleData": "{}",
             "templateId": "{{TestFixtures.TemplateId}}"}
            """);

        using var client = NewClient(handler);
        var version = await client.GetTemplateVersionAsync(TemplateId);
        Assert.Equal($"/v1/templates/{TemplateId}/versions/latest", handler.LastPath);
        Assert.Equal(7, version.VersionNumber);
        Assert.Equal(TemplateId, version.TemplateId);

        await client.GetTemplateVersionAsync(TemplateId, 3);
        Assert.Equal($"/v1/templates/{TemplateId}/versions/3", handler.LastPath);
    }

    [Fact]
    public async Task GetTemplateVersion_ParsesSampleData_AndFeedsItStraightBackToRender()
    {
        // sampleData arrives as a JSON string containing JSON; the SDK hands back a parsed
        // JsonElement so it can go straight into a render/validate call.
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, $$"""
            {"id": "550e8400-e29b-41d4-a716-446655440000", "versionNumber": 7,
             "templateJson": "{\"pages\":[]}", "sampleData": "{\"Title\":\"Invoice 1\",\"Total\":42}",
             "templateId": "{{TestFixtures.TemplateId}}"}
            """);

        using var client = NewClient(handler);
        var version = await client.GetTemplateVersionAsync(TemplateId);

        Assert.Equal(JsonValueKind.Object, version.SampleData.ValueKind);
        Assert.Equal("Invoice 1", version.SampleData.GetProperty("Title").GetString());
        Assert.Equal(42, version.SampleData.GetProperty("Total").GetInt32());
        // templateJson stays a raw string — sampleData is the only parsed free-form field.
        Assert.Equal("""{"pages":[]}""", version.TemplateJson);

        handler.Respond(200, """{"status": "ok", "documents": []}""");
        await client.RenderAsync(TemplateId, version.SampleData);
        Assert.Contains("\"Title\":\"Invoice 1\"", handler.LastRequestBody);
    }

    [Theory]
    [InlineData("\"\"")]                    // empty string
    [InlineData("\"not json at all\"")]     // malformed
    [InlineData("\"{oops\"")]               // malformed
    [InlineData("\"[1, 2, 3]\"")]           // valid JSON, but not an object
    [InlineData("\"42\"")]                  // valid JSON, but not an object
    [InlineData("null")]                    // JSON null
    public async Task GetTemplateVersion_SampleData_DecodesLenientlyToEmptyObject(string rawSampleData)
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, $$"""
            {"id": "550e8400-e29b-41d4-a716-446655440000", "versionNumber": 1,
             "templateJson": "{}", "sampleData": {{rawSampleData}},
             "templateId": "{{TestFixtures.TemplateId}}"}
            """);

        using var client = NewClient(handler);
        var version = await client.GetTemplateVersionAsync(TemplateId);

        Assert.Equal(JsonValueKind.Object, version.SampleData.ValueKind);
        Assert.Equal("{}", version.SampleData.GetRawText());
    }

    [Fact]
    public async Task GetTemplateVersion_SampleData_MissingKeyDefaultsToEmptyObject()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, $$"""
            {"id": "550e8400-e29b-41d4-a716-446655440000", "versionNumber": 1,
             "templateJson": "{}", "templateId": "{{TestFixtures.TemplateId}}"}
            """);

        using var client = NewClient(handler);
        var version = await client.GetTemplateVersionAsync(TemplateId);

        Assert.Equal(JsonValueKind.Object, version.SampleData.ValueKind);
        Assert.Equal("{}", version.SampleData.GetRawText());
    }

    [Fact]
    public async Task UpdateDocumentNameTemplate_SendsPatchWithBody()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, $$"""
            {"id": "550e8400-e29b-41d4-a716-446655440000", "versionNumber": 1,
             "templateJson": "{}", "sampleData": "{}", "documentNameTemplate": "Invoice {nr}",
             "templateId": "{{TestFixtures.TemplateId}}"}
            """);

        using var client = NewClient(handler);
        var version = await client.UpdateDocumentNameTemplateAsync(TemplateId, 1, "Invoice {nr}");

        Assert.Equal(HttpMethod.Patch, handler.LastMethod);
        Assert.Equal($"/v1/templates/{TemplateId}/versions/1/document-name-template", handler.LastPath);
        Assert.Equal("Invoice {nr}", version.DocumentNameTemplate);
        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        Assert.Equal("Invoice {nr}", body.GetProperty("documentNameTemplate").GetString());

        // null clears: the key must still be sent, with a JSON null.
        await client.UpdateDocumentNameTemplateAsync(TemplateId, 1, null);
        body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("documentNameTemplate").ValueKind);
    }

    [Fact]
    public async Task GetPreviewImageUrl_ReadsUrl_AndNullWhenMissing()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"url": "https://example.test/preview.png"}""");

        using var client = NewClient(handler);
        var url = await client.GetPreviewImageUrlAsync(TemplateId, 2);
        Assert.Equal($"/v1/templates/{TemplateId}/versions/2/preview-image", handler.LastPath);
        Assert.Equal("https://example.test/preview.png", url);

        handler.Respond(200, """{"url": null}""");
        Assert.Null(await client.GetPreviewImageUrlAsync(TemplateId, 2));
    }

    // ── Validate ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_JsonStringArray_IsTreatedAsBatch()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"issues": []}""");

        using var client = NewClient(handler);
        var result = await client.ValidateAsync(TemplateId, """[{"a": 1}, {"a": 2}]""", version: 1);

        Assert.Equal($"/v1/render/{TemplateId}/versions/1/validate", handler.LastPath);
        Assert.True(result.IsValid);
        var documents = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!).GetProperty("documents");
        Assert.Equal(2, documents.GetArrayLength());
        Assert.Equal(1, documents[0].GetProperty("a").GetInt32());
    }

    [Fact]
    public async Task Validate_ListOfJsonStrings_ParsesEachDocument()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"issues": []}""");

        using var client = NewClient(handler);
        await client.ValidateAsync(TemplateId, new List<string> { """{"a": 1}""", """{"a": 2}""" });

        Assert.Equal($"/v1/render/{TemplateId}/validate", handler.LastPath);
        var documents = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!).GetProperty("documents");
        Assert.Equal(2, documents.GetArrayLength());
        // Elements must be parsed objects, not JSON-encoded strings.
        Assert.Equal(JsonValueKind.Object, documents[0].ValueKind);
    }

    [Fact]
    public async Task Validate_ReadsIssues()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, $$"""
            {"issues": [
              {{TestFixtures.MakeIssueNode("Error", "MissingBinding", "customerName is missing", documentIndex: 0).ToJsonString()}}
            ]}
            """);

        using var client = NewClient(handler);
        var result = await client.ValidateAsync(TemplateId, new { Title = "x" });

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal(Models.RenderIssueType.MissingBinding, error.Type);
        Assert.Equal(0, error.DocumentIndex);
    }

    // ── Documents / fonts / organisation / meta ──────────────────────────────────

    [Fact]
    public async Task GetDocuments_ReturnsPagedResult()
    {
        var docNode = TestFixtures.MakeDocNode("Doc 0");
        docNode["isPdfDeleted"] = true;
        docNode["language"] = "nl";
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, $$"""{"items": [{{docNode.ToJsonString()}}], "total": 1, "skip": 0, "take": 50}""");

        using var client = NewClient(handler);
        var page = await client.GetDocumentsAsync();

        Assert.Equal("/v1/documents", handler.LastPath);
        var doc = Assert.Single(page.Items);
        Assert.Equal("Doc 0", doc.DocumentName);
        Assert.True(doc.IsPdfDeleted);
        Assert.Equal("nl", doc.Language);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task DownloadDocument_ReturnsBytes()
    {
        var pdf = Encoding.UTF8.GetBytes("%PDF-1.7 stored");
        var handler = new StubHttpMessageHandler();
        handler.RespondBytes(200, pdf, "application/pdf");

        using var client = NewClient(handler);
        var documentId = Guid.NewGuid();
        var bytes = await client.DownloadDocumentAsync(documentId);

        Assert.Equal($"/v1/documents/{documentId}/file", handler.LastPath);
        Assert.Equal(pdf, bytes);
    }

    [Fact]
    public async Task GetFonts_ReturnsList()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """["Inter", "Roboto"]""");

        using var client = NewClient(handler);
        var fonts = await client.GetFontsAsync();

        Assert.Equal("/v1/fonts", handler.LastPath);
        Assert.Equal(["Inter", "Roboto"], fonts);
    }

    [Fact]
    public async Task GetOrgStats_IncludesTokens()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """
            {"organisationName": "Acme", "tier": "pro",
             "periodStart": "2026-07-01T00:00:00Z", "periodEnd": "2026-08-01T00:00:00Z",
             "includedRendersPerMonth": 1000, "pagesUsedThisPeriod": 10, "pagesAvailable": 990,
             "includedTokensPerMonth": 50000, "tokensUsedThisPeriod": 1200, "tokensAvailable": 48800,
             "userCount": 4}
            """);

        using var client = NewClient(handler);
        var stats = await client.GetOrgStatsAsync();

        Assert.Equal("Acme", stats.OrganisationName);
        Assert.Equal(50000, stats.IncludedTokensPerMonth);
        Assert.Equal(1200, stats.TokensUsedThisPeriod);
        Assert.Equal(48800, stats.TokensAvailable);
    }

    [Fact]
    public async Task GetOrgStats_MissingCounts_AreNullNotZero()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"organisationName": "Acme", "tier": "pro"}""");

        using var client = NewClient(handler);
        var stats = await client.GetOrgStatsAsync();

        Assert.Null(stats.IncludedRendersPerMonth);
        Assert.Null(stats.PagesUsedThisPeriod);
        Assert.Null(stats.PagesAvailable);
        Assert.Null(stats.IncludedTokensPerMonth);
        Assert.Null(stats.TokensUsedThisPeriod);
        Assert.Null(stats.TokensAvailable);
        Assert.Null(stats.UserCount);
    }

    [Fact]
    public async Task GetStatus_ReturnsTrue()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"status": "healthy"}""");

        using var client = NewClient(handler);
        Assert.True(await client.GetStatusAsync());
        Assert.Equal("/v1/meta/status", handler.LastPath);
    }

    [Fact]
    public async Task GetVersion_ReadsVersion()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"version": "1.4.2"}""");

        using var client = NewClient(handler);
        Assert.Equal("1.4.2", await client.GetVersionAsync());
        Assert.Equal("/v1/meta/version", handler.LastPath);
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(401, typeof(PagrAuthenticationException))]
    [InlineData(403, typeof(PagrForbiddenException))]
    [InlineData(404, typeof(PagrNotFoundException))]
    [InlineData(413, typeof(PagrPayloadTooLargeException))]
    [InlineData(422, typeof(PagrValidationFailedException))]
    [InlineData(429, typeof(PagrRateLimitException))]
    // An unmapped status is the concrete PagrGenericApiException, never the abstract base.
    [InlineData(500, typeof(PagrGenericApiException))]
    public async Task ErrorStatus_MapsToTypedException(int status, Type expectedType)
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(status, """{"error": {"code": "SomeCode", "message": "went wrong"}}""");

        using var client = NewClient(handler);
        var ex = await Assert.ThrowsAnyAsync<PagrApiException>(() => client.GetTemplatesAsync());

        Assert.IsType(expectedType, ex);
        Assert.Equal(status, ex.StatusCode);
        Assert.Equal("SomeCode", ex.Code);
        Assert.Contains("went wrong", ex.Message);
    }

    [Fact]
    public async Task Status401_ThrowsAuthenticationException()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(401, """{"error": {"code": "Unauthorized", "message": "bad key"}}""");

        using var client = NewClient(handler);
        var ex = await Assert.ThrowsAsync<PagrAuthenticationException>(() => client.GetTemplatesAsync());

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("Unauthorized", ex.Code);
        Assert.Contains("bad key", ex.Message);
    }

    [Fact]
    public async Task NonJsonErrorBody_FallsBackToRawText()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(500, "gateway exploded", contentType: "text/plain");

        using var client = NewClient(handler);
        var ex = await Assert.ThrowsAsync<PagrGenericApiException>(() => client.GetTemplatesAsync());

        Assert.Equal(500, ex.StatusCode);
        Assert.Null(ex.Code);
        Assert.Contains("gateway exploded", ex.Message);
    }

    [Fact]
    public void PagrApiException_IsAbstract_SoNothingCanThrowTheBareBaseType()
    {
        Assert.True(typeof(PagrApiException).IsAbstract);
        Assert.True(typeof(PagrApiException).IsAssignableFrom(typeof(PagrGenericApiException)));
    }

    [Fact]
    public async Task SetApiKey_SwapsBearerTokenAtRuntime()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, """{"items": [], "total": 0, "skip": 0, "take": 0}""");

        using var client = NewClient(handler);
        await client.GetTemplatesAsync();
        Assert.Equal("Bearer key", handler.LastAuthorization);

        client.SetApiKey("rotated-key");
        await client.GetTemplatesAsync();
        Assert.Equal("Bearer rotated-key", handler.LastAuthorization);
    }
}
