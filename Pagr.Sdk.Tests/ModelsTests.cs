using System.Text;
using System.Text.Json;
using Pagr.Sdk.Exceptions;
using Pagr.Sdk.Models;
using Pagr.Sdk.Webhooks;
using Xunit;

namespace Pagr.Sdk.Tests;

public class ModelsTests
{
    // ── RenderIssue parsing ──────────────────────────────────────────────────────

    [Fact]
    public void RenderIssue_ParsesEnumsAndFields()
    {
        var json = TestFixtures.MakeIssueNode(
            "Warning", "MissingBinding", "customerName not bound", documentIndex: 2, elementId: "e1").ToJsonString();

        var issue = JsonSerializer.Deserialize<RenderIssue>(json, PagrJson.Options)!;

        Assert.Equal(RenderIssueType.MissingBinding, issue.Type);
        Assert.Equal(RenderIssueSeverity.Warning, issue.Severity);
        Assert.Equal("customerName not bound", issue.Description);
        Assert.Equal("e1", issue.ElementId);
        Assert.Equal(2, issue.DocumentIndex);
        Assert.False(issue.IsError);
    }

    [Fact]
    public void RenderIssue_UnknownValues_FailSafe()
    {
        // Unknown type fails open to Unknown; unknown severity fails closed to Error.
        var issue = JsonSerializer.Deserialize<RenderIssue>(
            """{"type": "BrandNewServerThing", "severity": "Catastrophic", "description": "x"}""",
            PagrJson.Options)!;
        Assert.Equal(RenderIssueType.Unknown, issue.Type);
        Assert.Equal(RenderIssueSeverity.Error, issue.Severity);

        // Missing/null values get the same fail-safe treatment.
        var bare = JsonSerializer.Deserialize<RenderIssue>(
            """{"type": null, "description": "x"}""", PagrJson.Options)!;
        Assert.Equal(RenderIssueType.Unknown, bare.Type);
        Assert.Equal(RenderIssueSeverity.Error, bare.Severity);
    }

    // ── RenderResult ─────────────────────────────────────────────────────────────

    [Fact]
    public void RenderResult_ReadsIssuesAndCounts()
    {
        var json = $$"""
            {"status": "ok", "requestedCount": 1, "renderedCount": 0, "missingCount": 1,
             "documents": [],
             "issues": [{{TestFixtures.MakeIssueNode("Error", "SchemaInvalid", "bad").ToJsonString()}}]}
            """;
        var result = RenderResult.FromApi(JsonSerializer.Deserialize<RenderApiResponse>(json, PagrJson.Options)!);

        Assert.False(result.Ok);
        Assert.Equal(1, result.MissingCount);
        var issue = Assert.Single(result.Issues);
        Assert.True(issue.IsError);
    }

    [Fact]
    public void RenderResult_MissingCounts_InfersDefaults()
    {
        // No counts in the body: a document present implies rendered=1/missing=0, requested defaults to 1.
        var json = $$"""{"status": "ok", "documents": [{{TestFixtures.MakeDocNode("Doc").ToJsonString()}}]}""";
        var result = RenderResult.FromApi(JsonSerializer.Deserialize<RenderApiResponse>(json, PagrJson.Options)!);
        Assert.Equal(1, result.RenderedCount);
        Assert.Equal(1, result.RequestedCount);
        Assert.Equal(0, result.MissingCount);

        var empty = RenderResult.FromApi(
            JsonSerializer.Deserialize<RenderApiResponse>("""{"status": "failed", "documents": []}""", PagrJson.Options)!);
        Assert.Equal(0, empty.RenderedCount);
        Assert.Equal(1, empty.MissingCount);
    }

    // ── BatchRenderResult correlation ────────────────────────────────────────────

    private static BatchRenderResult Batch(string json, IReadOnlyList<JsonElement>? inputs = null) =>
        BatchRenderResult.FromApi(JsonSerializer.Deserialize<RenderApiResponse>(json, PagrJson.Options)!, inputs);

    [Fact]
    public void Batch_AllSuccess()
    {
        var result = Batch($$"""
            {"status": "ok", "renderedCount": 2, "requestedCount": 2, "missingCount": 0,
             "documents": [
               {{TestFixtures.MakeDocNode("Doc 0", documentIndex: 0).ToJsonString()}},
               {{TestFixtures.MakeDocNode("Doc 1", documentIndex: 1).ToJsonString()}}
             ]}
            """);

        Assert.True(result.Ok);
        Assert.Equal(2, result.Succeeded.Count);
        Assert.Empty(result.Failed);
        Assert.Equal(["Doc 0", "Doc 1"], result.Documents.Select(d => d.DocumentName));
    }

    [Fact]
    public void Batch_ErrorIssue_CorrelatesByIndex()
    {
        var result = Batch($$"""
            {"status": "ok", "renderedCount": 1, "requestedCount": 2, "missingCount": 1,
             "documents": [{{TestFixtures.MakeDocNode("Doc 0", documentIndex: 0).ToJsonString()}}],
             "issues": [{{TestFixtures.MakeIssueNode("Error", "SchemaInvalid", "bad", documentIndex: 1).ToJsonString()}}]}
            """);

        Assert.True(result[0].Ok);
        Assert.False(result[1].Ok);
        Assert.Equal("bad", Assert.Single(result[1].Issues).Description);
    }

    [Fact]
    public void Batch_PlacesEachDocumentAtItsOwnDocumentIndex()
    {
        // The documents arrive out of request order; each still lands on the slot it reports.
        var result = Batch($$"""
            {"status": "ok", "renderedCount": 3, "requestedCount": 3, "missingCount": 0,
             "documents": [
               {{TestFixtures.MakeDocNode("Doc 2", documentIndex: 2).ToJsonString()}},
               {{TestFixtures.MakeDocNode("Doc 0", documentIndex: 0).ToJsonString()}},
               {{TestFixtures.MakeDocNode("Doc 1", documentIndex: 1).ToJsonString()}}
             ]}
            """);

        Assert.Equal(["Doc 0", "Doc 1", "Doc 2"], result.Select(it => it.Document!.DocumentName));
        Assert.Equal([0, 1, 2], result.Select(it => it.Document!.DocumentIndex));
    }

    [Fact]
    public void Batch_CorrelatesByIndex_WhenWarningBlockedLeavesAGap()
    {
        // Regression: a Warning-blocked document at index 1 renders nothing yet carries no
        // Error-severity issue, so nothing marks its slot failed. Positional filling slid
        // Doc 2 up into slot 1; index-based placement must keep it at slot 2.
        var result = Batch($$"""
            {"status": "partial", "renderedCount": 2, "requestedCount": 3, "missingCount": 1,
             "documents": [
               {{TestFixtures.MakeDocNode("Doc 0", documentIndex: 0).ToJsonString()}},
               {{TestFixtures.MakeDocNode("Doc 2", documentIndex: 2).ToJsonString()}}
             ],
             "issues": [{{TestFixtures.MakeIssueNode("Warning", "MissingBinding", "missing binding", documentIndex: 1).ToJsonString()}}]}
            """);

        Assert.Equal("Doc 0", result[0].Document!.DocumentName);
        Assert.Equal("Doc 2", result[2].Document!.DocumentName);
        Assert.Null(result[1].Document);
        Assert.Equal("missing binding", Assert.Single(result[1].Issues).Description);
    }

    [Fact]
    public void Batch_DropsDocumentWithoutDocumentIndex()
    {
        // documentIndex is the only correlation: a document that omits it is dropped rather
        // than guessed onto a slot by position.
        var result = Batch($$"""
            {"status": "ok", "renderedCount": 2, "requestedCount": 2, "missingCount": 0,
             "documents": [
               {{TestFixtures.MakeDocNode("Doc 0", documentIndex: 0).ToJsonString()}},
               {{TestFixtures.MakeDocNode("Doc 1").ToJsonString()}}
             ]}
            """);

        Assert.Equal("Doc 0", result[0].Document!.DocumentName);
        Assert.Null(result[1].Document);
        Assert.Equal("not rendered", Assert.Single(result[1].Issues).Description);
    }

    [Fact]
    public void Batch_OutOfRangeDocumentIndex_IsDroppedNotThrown()
    {
        var result = Batch($$"""
            {"status": "ok", "renderedCount": 1, "requestedCount": 1, "missingCount": 0,
             "documents": [{{TestFixtures.MakeDocNode("Doc 0", documentIndex: 7).ToJsonString()}}]}
            """);

        var item = Assert.Single(result);
        Assert.Null(item.Document);
        Assert.Equal("not rendered", Assert.Single(item.Issues).Description);
    }

    [Fact]
    public void Batch_MissingCount_IsComputedNotRead()
    {
        // MissingCount is by definition RequestedCount - RenderedCount, so a response that
        // disagrees with itself cannot report a short batch as Ok.
        var result = Batch($$"""
            {"status": "ok", "renderedCount": 2, "requestedCount": 3, "missingCount": 0,
             "documents": [
               {{TestFixtures.MakeDocNode("Doc 0", documentIndex: 0).ToJsonString()}},
               {{TestFixtures.MakeDocNode("Doc 1", documentIndex: 1).ToJsonString()}}
             ]}
            """);

        Assert.Equal(1, result.MissingCount);
        Assert.False(result.Ok);
    }

    [Fact]
    public void Batch_Ok_IsDerivedFromTheCounts_NotTheItems()
    {
        var result = Batch($$"""
            {"status": "ok", "renderedCount": 2, "requestedCount": 2,
             "documents": [{{TestFixtures.MakeDocNode("Doc 0", documentIndex: 0).ToJsonString()}}]}
            """);

        Assert.Equal(0, result.MissingCount);
        Assert.True(result.Ok);
        // The per-item view stays honest about the slot that carries no document.
        Assert.Equal([1], result.Failed.Select(it => it.Index));
    }

    [Fact]
    public void Batch_BatchWideIssue_AttachesToAllItems()
    {
        var result = Batch($$"""
            {"status": "failed", "renderedCount": 0, "requestedCount": 2, "missingCount": 2,
             "documents": [],
             "issues": [{{TestFixtures.MakeIssueNode("Error", "InvalidLayout", "template broken").ToJsonString()}}]}
            """);

        Assert.Equal(2, result.Count);
        Assert.All(result, item =>
        {
            Assert.False(item.Ok);
            Assert.Equal("template broken", Assert.Single(item.Issues).Description);
        });
    }

    [Fact]
    public void Batch_InsufficientCredit_IsData()
    {
        var result = Batch("""
            {"status": "insufficient_credit", "message": "out of credit",
             "renderedCount": 0, "requestedCount": 2, "missingCount": 2, "documents": []}
            """);

        Assert.True(result.InsufficientCredit);
        Assert.False(result.Ok);
        // Unexplained empty slots get a synthetic "not rendered" error issue.
        Assert.All(result, item =>
        {
            var issue = Assert.Single(item.Issues);
            Assert.Equal(RenderIssueType.Unknown, issue.Type);
            Assert.Equal("not rendered", issue.Description);
        });
    }

    [Fact]
    public void Batch_WithoutInputs_UsesRequestedCount()
    {
        var result = Batch($$"""
            {"status": "ok", "renderedCount": 1, "requestedCount": 2, "missingCount": 1,
             "documents": [{{TestFixtures.MakeDocNode("Doc 0", documentIndex: 0).ToJsonString()}}]}
            """);

        Assert.Equal(2, result.Count);
        Assert.Equal("Doc 0", result[0].Document!.DocumentName);
        Assert.Null(result[0].Input);
        Assert.Equal("not rendered", Assert.Single(result[1].Issues).Description);
    }

    // ── ValidationResponse ───────────────────────────────────────────────────────

    private static ValidationResponse Validation(string json) =>
        ValidationResponse.FromApi(JsonSerializer.Deserialize<ValidationApiResponse>(json, PagrJson.Options)!);

    [Fact]
    public void ValidationResponse_ParsesIssues()
    {
        var resp = Validation($$"""
            {"issues": [
              {{TestFixtures.MakeIssueNode("Error", "MissingBinding", "customerName missing", documentIndex: 1).ToJsonString()}},
              {{TestFixtures.MakeIssueNode("Warning", "UnformattedValue", "odd date").ToJsonString()}}
            ]}
            """);

        Assert.False(resp.IsValid);
        Assert.Equal(2, resp.Count);
        Assert.Single(resp.Errors);
        Assert.Single(resp.Warnings);

        // IssuesFor includes batch-wide issues (documentIndex null).
        Assert.Equal(2, resp.IssuesFor(1).Count);
        Assert.Single(resp.IssuesFor(0));
    }

    [Fact]
    public void ValidationResponse_ValidWhenOnlyInformationIssue()
    {
        var resp = Validation($$"""
            {"issues": [{{TestFixtures.MakeIssueNode("Information", "UnformattedValue", "fyi").ToJsonString()}}]}
            """);

        Assert.True(resp.IsValid);
        Assert.Empty(resp.Errors);
    }

    [Fact]
    public void ValidationResponse_EmptyIsValid()
    {
        var resp = Validation("""{"issues": []}""");
        Assert.True(resp.IsValid);
        Assert.Empty(resp);
    }

    [Fact]
    public void ValidationResponse_InvalidWhenWarningOnly()
    {
        // IsValid is the production gate: any issue >= Warning invalidates, even with
        // no Error present.
        var resp = Validation($$"""
            {"issues": [{{TestFixtures.MakeIssueNode("Warning", "MissingBinding", "odd date").ToJsonString()}}]}
            """);

        Assert.False(resp.IsValid);
        Assert.Empty(resp.Errors);
        Assert.Single(resp.Warnings);
    }

    // ── Callback parsing ─────────────────────────────────────────────────────────

    [Fact]
    public void ParseCallback_RoutesProgress()
    {
        var payload = $$"""
            {
              "jobId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
              "processed": 2,
              "requestedCount": 5,
              "documentIndex": 4,
              "document": {{TestFixtures.MakeDocNode("Doc 2").ToJsonString()}}
            }
            """;

        var cb = RenderCallback.Parse(payload);

        var progress = Assert.IsType<RenderProgress>(cb);
        Assert.Equal(2, progress.Processed);
        Assert.Equal(4, progress.DocumentIndex);
        Assert.Equal(40.0, progress.ProgressPct);
        Assert.Equal("Doc 2", progress.Document.DocumentName);
    }

    [Fact]
    public void ParseCallback_RoutesCompletionWithMissingCount()
    {
        const string payload = """
            {
              "jobId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
              "state": "completed",
              "status": "ok",
              "renderedCount": 4,
              "requestedCount": 5,
              "missingCount": 1,
              "message": null
            }
            """;

        var cb = RenderCallback.Parse(payload);

        var completion = Assert.IsType<RenderCompletion>(cb);
        Assert.True(completion.Ok);
        Assert.Equal(RenderJobState.Completed, completion.State);
        Assert.Equal(4, completion.RenderedCount);
        Assert.Equal(1, completion.MissingCount);
    }

    // ── RenderJob / RenderJobStatus ──────────────────────────────────────────────

    [Fact]
    public void RenderJob_ParsesCamelCase()
    {
        var job = JsonSerializer.Deserialize<RenderJob>(
            """{"jobId": "f47ac10b-58cc-4372-a567-0e02b2c3d479", "requestedCount": 3, "state": "queued"}""",
            PagrJson.Options)!;

        Assert.Equal(3, job.RequestedCount);
        Assert.Equal(RenderJobState.Queued, job.State);
    }

    [Fact]
    public void RenderJobStatus_ParsesZSuffixTimestamps()
    {
        var status = JsonSerializer.Deserialize<RenderJobStatus>(
            """
            {"jobId": "f47ac10b-58cc-4372-a567-0e02b2c3d479", "state": "pending",
             "renderedCount": 1, "startedAt": "2026-07-16T10:00:00Z"}
            """, PagrJson.Options)!;

        Assert.False(status.Done);
        Assert.Null(status.Status);
        Assert.Equal(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero), status.StartedAt);
        Assert.Null(status.CompletedAt);
    }

    [Fact]
    public void RenderJobStatus_OffsetlessTimestamps_AreTreatedAsUtc()
    {
        var status = JsonSerializer.Deserialize<RenderJobStatus>(
            """
            {"jobId": "f47ac10b-58cc-4372-a567-0e02b2c3d479", "state": "failed", "status": "failed",
             "renderedCount": 0, "startedAt": "2026-07-16T10:00:00",
             "failureReason": "boom"}
            """, PagrJson.Options)!;

        Assert.True(status.Done);
        Assert.False(status.Ok);
        Assert.Equal("boom", status.FailureReason);
        Assert.Equal(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero), status.StartedAt);
    }

    [Fact]
    public void RenderJobState_UnknownValue_FailsOpenAndIsTerminal()
    {
        var state = JsonSerializer.Deserialize<RenderJobState>("\"cancelled\"", PagrJson.Options);
        Assert.Equal(RenderJobState.Unknown, state);
        Assert.True(state.IsTerminal());
        Assert.False(RenderJobState.Pending.IsTerminal());
        Assert.False(RenderJobState.Queued.IsTerminal());
        Assert.True(RenderJobState.Completed.IsTerminal());
        Assert.True(RenderJobState.Failed.IsTerminal());
    }

    [Fact]
    public void RenderOutcome_UnknownValue_FailsOpen()
    {
        var outcome = JsonSerializer.Deserialize<RenderOutcome>("\"something_new\"", PagrJson.Options);
        Assert.Equal(RenderOutcome.Unknown, outcome);

        var insufficientCredit = JsonSerializer.Deserialize<RenderOutcome>("\"insufficient_credit\"", PagrJson.Options);
        Assert.Equal(RenderOutcome.InsufficientCredit, insufficientCredit);
    }

    [Fact]
    public void RenderIssueSeverity_IsAtLeast_AndIsBlockingProduction()
    {
        Assert.True(RenderIssueSeverity.Error.IsAtLeast(RenderIssueSeverity.Warning));
        Assert.False(RenderIssueSeverity.Information.IsAtLeast(RenderIssueSeverity.Warning));
        Assert.True(RenderIssueSeverity.Warning.IsBlockingProduction());
        Assert.False(RenderIssueSeverity.Information.IsBlockingProduction());
    }

    // ── RenderedDocument bytes/save ──────────────────────────────────────────────

    [Fact]
    public async Task RenderedDocument_ToBytesAndSave()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello pdf"));
        var doc = TestFixtures.MakeDoc("file.pdf", payload);

        Assert.Equal(Encoding.UTF8.GetBytes("hello pdf"), doc.ToBytes());

        var dir = Directory.CreateTempSubdirectory("pagr-test-");
        try
        {
            var written = await doc.SaveAsync(dir.FullName);
            Assert.EndsWith("file.pdf", written);
            Assert.Equal(Encoding.UTF8.GetBytes("hello pdf"), await File.ReadAllBytesAsync(written));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_AppendsPdfExtensionWhenDirectoryAndNameHasNone()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("x"));
        var doc = TestFixtures.MakeDoc("invoice-123", payload); // no extension

        var dir = Directory.CreateTempSubdirectory("pagr-test-");
        try
        {
            var written = await doc.SaveAsync(dir.FullName);
            Assert.Equal(Path.Combine(dir.FullName, "invoice-123.pdf"), written);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ToBytes_WithoutContent_Throws()
    {
        var doc = TestFixtures.MakeDoc("file.pdf"); // no base64
        // PagrApiException is abstract, so this asserts the documented catch-all contract.
        Assert.ThrowsAny<PagrApiException>(() => doc.ToBytes());
    }

    [Fact]
    public void RenderedDocument_ReadsDocumentIndex()
    {
        Assert.Equal(3, TestFixtures.MakeDoc("Doc", documentIndex: 3).DocumentIndex);
        // Absent outside a render response (e.g. the document-listing endpoints).
        Assert.Null(TestFixtures.MakeDoc("Doc").DocumentIndex);
    }

    [Fact]
    public void RenderedDocument_ReadsLanguage()
    {
        var node = TestFixtures.MakeDocNode("Doc");
        node["language"] = "nl-BE";
        var doc = JsonSerializer.Deserialize<RenderedDocument>(node.ToJsonString(), PagrJson.Options)!;
        Assert.Equal("nl-BE", doc.Language);

        // Null when the template has no translations, or the render chose none.
        Assert.Null(TestFixtures.MakeDoc("Doc").Language);
    }

    // ── Filename safety (save-to-disk) ───────────────────────────────────────────

    [Theory]
    // Directory components go, both separators, on every platform.
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\Windows\\System32\\evil", "evil")]
    [InlineData("/etc/passwd", "passwd")]
    [InlineData("a/b/c.pdf", "c.pdf")]
    [InlineData("a\\b\\c.pdf", "c.pdf")]
    [InlineData("sub/dir/report", "report")]
    [InlineData("sub\\dir\\report", "report")]
    // Windows drive prefixes go too, absolute and drive-relative.
    [InlineData("C:\\Windows\\evil.pdf", "evil.pdf")]
    [InlineData("C:evil.pdf", "evil.pdf")]
    // Nothing usable left → a literal fallback, never an empty path segment.
    [InlineData("", "document")]
    [InlineData("   ", "document")]
    [InlineData("/", "document")]
    [InlineData(".", "document")]
    [InlineData("..", "document")]
    // An ordinary name is left completely alone — including an embedded dot.
    [InlineData("Invoice 2024.10", "Invoice 2024.10")]
    [InlineData("facture-café-№7", "facture-café-№7")]
    public void SafeFilename_ReducesNameToASingleSafeSegment(string input, string expected)
        => Assert.Equal(expected, DocumentContent.SafeFilename(input));

    [Theory]
    // A dot in the name is not an extension: the check is a case-insensitive .pdf suffix test.
    [InlineData("Invoice 2024.10", "Invoice 2024.10.pdf")]
    [InlineData("invoice-123", "invoice-123.pdf")]
    [InlineData("file.pdf", "file.pdf")]
    [InlineData("FILE.PDF", "FILE.PDF")]
    [InlineData("report.docx", "report.docx.pdf")]
    public void WithPdfSuffix_AppendsOnlyWhenNotAlreadyPdf(string input, string expected)
        => Assert.Equal(expected, DocumentContent.WithPdfSuffix(input));

    [Fact]
    public async Task SaveAsync_EmbeddedDotInName_StillGetsPdfAppended()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("x"));
        var doc = TestFixtures.MakeDoc("Invoice 2024.10", payload);

        var dir = Directory.CreateTempSubdirectory("pagr-test-");
        try
        {
            var written = await doc.SaveAsync(dir.FullName);
            Assert.Equal(Path.Combine(dir.FullName, "Invoice 2024.10.pdf"), written);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("../../evil", "evil.pdf")]
    [InlineData("C:\\Windows\\Temp\\evil.pdf", "evil.pdf")]
    [InlineData("/tmp/pagr_absolute", "pagr_absolute.pdf")]
    public async Task SaveAsync_CraftedName_CannotEscapeTheTargetDirectory(string documentName, string expectedFile)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-1.7"));
        var doc = TestFixtures.MakeDoc(documentName, payload);

        var dir = Directory.CreateTempSubdirectory("pagr-test-");
        try
        {
            var written = await doc.SaveAsync(dir.FullName);

            Assert.Equal(dir.FullName, Path.GetDirectoryName(written));
            Assert.Equal(expectedFile, Path.GetFileName(written));
            Assert.True(File.Exists(written));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // ── PdfDocument ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("attachment; filename=\"Doc 0.pdf\"", "Doc 0")]
    [InlineData("attachment; filename=Doc 0.pdf", "Doc 0")]
    [InlineData("attachment; filename=\"Invoice 2024.10.pdf\"; size=42", "Invoice 2024.10")]
    [InlineData("attachment; filename=\"no-extension\"", "no-extension")]
    // HTTP header parameter names are case-insensitive (RFC 6266) — all six SDKs match this way.
    [InlineData("attachment; Filename=\"Doc 0.pdf\"", "Doc 0")]
    [InlineData("attachment; FILENAME=\"Doc 0.pdf\"", "Doc 0")]
    [InlineData("attachment; filename=\"\"", "document")]
    [InlineData("attachment; filename=\".pdf\"", "document")]
    [InlineData("attachment", "document")]
    [InlineData("", "document")]
    [InlineData(null, "document")]
    public void PdfDocument_FilenameFromContentDisposition(string? header, string expected)
        => Assert.Equal(expected, PdfDocument.FilenameFromContentDisposition(header));

    [Fact]
    public async Task PdfDocument_SaveAsync_SanitizesAndAppendsPdf()
    {
        var pdf = Encoding.UTF8.GetBytes("%PDF-1.7 raw");
        var doc = new PdfDocument { DocumentName = "../../evil", Content = pdf };

        var dir = Directory.CreateTempSubdirectory("pagr-test-");
        try
        {
            var written = await doc.SaveAsync(dir.FullName);

            Assert.Equal(Path.Combine(dir.FullName, "evil.pdf"), written);
            Assert.Equal(pdf, await File.ReadAllBytesAsync(written));
            Assert.Equal(pdf, doc.ToBytes());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void PdfRenderResult_FromErrorEnvelope_IsLenient()
    {
        // A truncated envelope must still produce a usable "did not render" result.
        var bare = PdfRenderResult.FromErrorEnvelope(JsonSerializer.Deserialize<JsonElement>("{}"));
        Assert.False(bare.Ok);
        Assert.Equal("failed", bare.Status);
        Assert.Null(bare.Message);
        Assert.Empty(bare.Issues);

        var notAnObject = PdfRenderResult.FromErrorEnvelope(JsonSerializer.Deserialize<JsonElement>("[]"));
        Assert.False(notAnObject.Ok);
        Assert.Equal("failed", notAnObject.Status);
    }

    // ── PagedResult ──────────────────────────────────────────────────────────────

    [Fact]
    public void PagedResult_HasMore()
    {
        var page = JsonSerializer.Deserialize<PagedResult<string>>(
            """{"items": ["a", "b"], "total": 5, "skip": 0, "take": 2}""", PagrJson.Options)!;
        Assert.True(page.HasMore);
        Assert.Equal(2, page.Count);
        Assert.Equal("b", page[1]);

        var lastPage = JsonSerializer.Deserialize<PagedResult<string>>(
            """{"items": ["e"], "total": 5, "skip": 4, "take": 2}""", PagrJson.Options)!;
        Assert.False(lastPage.HasMore);
    }
}
