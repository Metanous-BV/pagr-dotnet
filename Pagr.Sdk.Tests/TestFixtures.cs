using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pagr.Sdk.Models;

namespace Pagr.Sdk.Tests;

internal static class TestFixtures
{
    public const string TemplateId = "8e392ab3-9064-438c-b0b3-86dcd9844d38";

    /// <summary>Builds a minimal rendered-document JSON object, mirroring the API shape.</summary>
    /// <remarks>
    /// Set <paramref name="documentIndex"/> so batch tests can return documents out of order
    /// and assert index-based correlation; omit it to assert the document is dropped.
    /// </remarks>
    public static JsonObject MakeDocNode(string name, string? base64 = null, int? documentIndex = null)
    {
        var node = new JsonObject
        {
            ["id"] = "550e8400-e29b-41d4-a716-446655440000",
            ["documentName"] = name,
            ["templateId"] = "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
            ["versionNumber"] = 1,
            ["environment"] = "test",
            ["fileSizeBytes"] = 1024,
            ["pageCount"] = 1,
            ["renderedAt"] = "2026-06-10T12:34:56+00:00",
            ["renderDuration"] = 12.5,
            ["viewUrl"] = "https://example.test/doc",
            ["documentType"] = "Invoice",
        };
        if (base64 is not null)
            node["documentBase64"] = base64;
        if (documentIndex is not null)
            node["documentIndex"] = documentIndex;
        return node;
    }

    public static RenderedDocument MakeDoc(string name, string? base64 = null, int? documentIndex = null) =>
        JsonSerializer.Deserialize<RenderedDocument>(
            MakeDocNode(name, base64, documentIndex).ToJsonString(), PagrJson.Options)!;

    /// <summary>Builds a render/validation issue JSON object, mirroring the API shape.</summary>
    public static JsonObject MakeIssueNode(
        string severity, string type, string description, int? documentIndex = null, string? elementId = null)
    {
        var node = new JsonObject
        {
            ["severity"] = severity,
            ["type"] = type,
            ["description"] = description,
        };
        if (documentIndex is not null)
            node["documentIndex"] = documentIndex;
        if (elementId is not null)
            node["elementId"] = elementId;
        return node;
    }
}

/// <summary>An <see cref="HttpMessageHandler"/> that returns a pre-configured response and records the last request.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private sealed record QueuedResponse(int Status, byte[]? Bytes, string? Body, string ContentType, Dictionary<string, string>? Headers);

    private readonly Queue<QueuedResponse> _queue = new();
    private readonly Queue<Func<Exception>> _exceptionQueue = new();

    public int ResponseStatus { get; set; } = 200;
    public string ResponseBody { get; set; } = "{}";
    public byte[]? ResponseBytes { get; set; }
    public string ResponseContentType { get; set; } = "application/json";
    public Dictionary<string, string>? ResponseHeaders { get; set; }
    public HttpMethod? LastMethod { get; private set; }
    public string? LastPath { get; private set; }
    public string? LastQuery { get; private set; }
    public string? LastRequestBody { get; private set; }
    public string? LastAuthorization { get; private set; }
    public string? LastAccept { get; private set; }
    public int CallCount { get; private set; }

    public void Respond(
        int status, string body, string contentType = "application/json",
        Dictionary<string, string>? headers = null)
    {
        ResponseStatus = status;
        ResponseBody = body;
        ResponseBytes = null;
        ResponseContentType = contentType;
        ResponseHeaders = headers;
    }

    public void RespondBytes(
        int status, byte[] bytes, string contentType, Dictionary<string, string>? headers = null)
    {
        ResponseStatus = status;
        ResponseBytes = bytes;
        ResponseContentType = contentType;
        ResponseHeaders = headers;
    }

    /// <summary>Queues a JSON response to be returned on the Nth call (FIFO), before falling back to <see cref="Respond"/>.</summary>
    public void EnqueueResponse(int status, string body, Dictionary<string, string>? headers = null) =>
        _queue.Enqueue(new QueuedResponse(status, null, body, "application/json", headers));

    /// <summary>Queues a transport-level failure (e.g. a timeout or connection error) for the Nth call.</summary>
    public void EnqueueException(Func<Exception> exceptionFactory) => _exceptionQueue.Enqueue(exceptionFactory);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastMethod = request.Method;
        LastPath = request.RequestUri?.AbsolutePath;
        LastQuery = request.RequestUri is { } uri ? Uri.UnescapeDataString(uri.Query) : null;
        LastAuthorization = request.Headers.Authorization?.ToString();
        LastAccept = request.Headers.Accept.ToString();
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        if (_exceptionQueue.Count > 0)
            throw _exceptionQueue.Dequeue()();

        if (_queue.Count > 0)
        {
            var queued = _queue.Dequeue();
            var queuedContent = queued.Bytes is not null
                ? new ByteArrayContent(queued.Bytes)
                : new StringContent(queued.Body ?? "", Encoding.UTF8, queued.ContentType);
            if (queued.Bytes is not null)
                queuedContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(queued.ContentType);
            var queuedResponse = new HttpResponseMessage((HttpStatusCode)queued.Status) { Content = queuedContent };
            AddHeaders(queuedResponse, queued.Headers);
            return queuedResponse;
        }

        HttpContent content = ResponseBytes is not null
            ? new ByteArrayContent(ResponseBytes)
            : new StringContent(ResponseBody, Encoding.UTF8, ResponseContentType);
        if (ResponseBytes is not null)
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ResponseContentType);

        var response = new HttpResponseMessage((HttpStatusCode)ResponseStatus) { Content = content };
        AddHeaders(response, ResponseHeaders);
        return response;
    }

    /// <summary>
    /// Adds stubbed headers, routing content headers (e.g. <c>Content-Disposition</c>) to the
    /// content's collection the way a real <see cref="HttpClient"/> response does.
    /// </summary>
    private static void AddHeaders(HttpResponseMessage response, Dictionary<string, string>? headers)
    {
        if (headers is null)
            return;
        foreach (var (key, value) in headers)
        {
            if (!response.Headers.TryAddWithoutValidation(key, value))
                response.Content.Headers.TryAddWithoutValidation(key, value);
        }
    }
}
