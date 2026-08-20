using System.Net;
using System.Net.Sockets;
using System.Text;
using Pagr.Sdk.Exceptions;
using Xunit;

namespace Pagr.Sdk.Tests;

/// <summary>
/// Covers the HTTP transport behaviors added alongside the C# port of the retry/timeout/decode
/// parity work: decode-failure translation, GET-only retry with backoff/<c>Retry-After</c>,
/// rate-limit surfacing, and redirect-following.
/// </summary>
public class HttpTransportTests
{
    private static readonly Guid TemplateId = Guid.Parse(TestFixtures.TemplateId);

    private static PagrApiClient NewClient(StubHttpMessageHandler handler, int maxRetries = 2) =>
        new(new PagrClientOptions { BaseUrl = "http://localhost", ApiKey = "key", MaxRetries = maxRetries }, handler);

    private static PagrApiClient NoBackoff(PagrApiClient client)
    {
        client.Transport.BackoffBase = TimeSpan.Zero;
        client.Transport.BackoffMax = TimeSpan.Zero;
        return client;
    }

    // ── Decode failures surface as PagrDecodeException ────────────────────────────

    [Fact]
    public async Task EmptyBody_RaisesPagrDecodeException()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, "");
        using var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<PagrDecodeException>(() => client.GetTemplateAsync(TemplateId));
        Assert.IsAssignableFrom<PagrApiException>(ex);
    }

    [Fact]
    public async Task MissingRequiredField_RaisesPagrDecodeException()
    {
        // documentName is required; omitting it must raise PagrDecodeException, not a raw JsonException.
        var doc = TestFixtures.MakeDocNode("x");
        doc.Remove("documentName");
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, doc.ToJsonString());
        using var client = NewClient(handler);

        await Assert.ThrowsAsync<PagrDecodeException>(() => client.GetDocumentAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task MalformedCallbackShape_RaisesPagrDecodeException()
    {
        // Neither a progress nor a completion shape (missing every correlation field).
        Assert.Throws<PagrDecodeException>(() => Webhooks.RenderCallback.Parse("""{"jobId": "f47ac10b-58cc-4372-a567-0e02b2c3d479"}"""));
    }

    [Fact]
    public void NonJsonWebhookBody_RaisesPagrDecodeException()
    {
        Assert.Throws<PagrDecodeException>(() => Webhooks.RenderCallback.Parse("not json"));
    }

    // ── Non-raising statuses ───────────────────────────────────────────────────────

    [Fact]
    public async Task NonRaisingStatus_ReturnsResponseInsteadOfThrowing()
    {
        // 422 would normally map to PagrValidationFailedException. Declared non-raising, the
        // transport hands the response back so the caller can read the body *and* the headers
        // off it (this is what RenderPdfAsync's 422 business outcome relies on).
        var handler = new StubHttpMessageHandler();
        handler.Respond(422, """{"status": "failed"}""", headers: new() { ["X-Pagr-Issue-Count"] = "3" });
        using var client = NoBackoff(NewClient(handler));

        using var response = await client.Transport.PostAsync(
            "v1/render", new { documents = Array.Empty<int>() },
            nonRaisingStatuses: new HashSet<int> { 422 });

        Assert.Equal(422, (int)response.StatusCode);
        Assert.Equal("""{"status": "failed"}""", await response.Content.ReadAsStringAsync());
        Assert.Equal("3", response.Headers.GetValues("X-Pagr-Issue-Count").Single());
    }

    [Fact]
    public async Task NonRaisingStatus_OtherStatusesStillThrow()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(404, """{"error": {"code": "NotFound", "message": "nope"}}""");
        using var client = NoBackoff(NewClient(handler));

        await Assert.ThrowsAsync<PagrNotFoundException>(() => client.Transport.PostAsync(
            "v1/render", new { documents = Array.Empty<int>() },
            nonRaisingStatuses: new HashSet<int> { 422 }));
    }

    [Fact]
    public async Task AcceptHeader_IsOverridableAndDefaultsToJson()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(200, "{}");
        using var client = NoBackoff(NewClient(handler));

        (await client.Transport.PostAsync("v1/render", new { })).Dispose();
        Assert.Equal("application/json", handler.LastAccept);

        (await client.Transport.PostAsync(
            "v1/render", new { }, headers: new Dictionary<string, string> { ["Accept"] = "application/pdf" }))
            .Dispose();
        // The override replaces the default rather than being appended alongside it.
        Assert.Equal("application/pdf", handler.LastAccept);
    }

    // ── Retries (idempotent GET only) ──────────────────────────────────────────────

    [Fact]
    public async Task Get_RetriesOn503_ThenSucceeds()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(503, "");
        handler.Respond(200, """{"version": "1.0.0"}""");
        using var client = NoBackoff(NewClient(handler));

        var version = await client.GetVersionAsync();

        Assert.Equal("1.0.0", version);
        Assert.Equal(2, handler.CallCount);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(504)]
    public async Task Get_RetriesOnAllRetriable5xx(int status)
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(status, "");
        handler.Respond(200, """{"version": "1.0.0"}""");
        using var client = NoBackoff(NewClient(handler));

        var version = await client.GetVersionAsync();

        Assert.Equal("1.0.0", version);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Get_RetriesExhausted_RaisesTypedError()
    {
        // max_retries defaults to 2 -> 3 attempts, all 503 -> the typed error surfaces.
        var handler = new StubHttpMessageHandler();
        handler.Respond(503, "");
        using var client = NoBackoff(NewClient(handler));

        var ex = await Assert.ThrowsAsync<PagrGenericApiException>(() => client.GetVersionAsync());

        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task Get_429_IsNotRetried()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(429, """{"error": {"code": "RateLimit", "message": "slow down"}}""");
        using var client = NoBackoff(NewClient(handler));

        var ex = await Assert.ThrowsAsync<PagrRateLimitException>(() => client.GetVersionAsync());

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(429, ex.StatusCode);
    }

    [Fact]
    public async Task Post_IsNeverRetried_OnRetriableStatus()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(503, """{"error": {"code": "QueueFull", "message": "full"}}""");
        using var client = NoBackoff(NewClient(handler));

        var ex = await Assert.ThrowsAsync<PagrGenericApiException>(
            () => client.RenderAsync(TemplateId, """{"a": 1}"""));

        Assert.Equal(1, handler.CallCount);
        Assert.Equal("QueueFull", ex.Code);
    }

    [Fact]
    public async Task MaxRetriesZero_DisablesRetry()
    {
        var handler = new StubHttpMessageHandler();
        handler.Respond(503, "");
        using var client = NoBackoff(NewClient(handler, maxRetries: 0));

        await Assert.ThrowsAsync<PagrGenericApiException>(() => client.GetVersionAsync());

        Assert.Equal(1, handler.CallCount);
    }

    // ── Transport failures wrapped in the PagrApiException tree ───────────────────

    [Fact]
    public async Task Get_RetriesOnTimeout_ThenSucceeds()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueException(() => new TaskCanceledException("simulated timeout"));
        handler.Respond(200, """{"version": "1.0.0"}""");
        using var client = NoBackoff(NewClient(handler));

        var version = await client.GetVersionAsync();

        Assert.Equal("1.0.0", version);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Get_RetriesOnConnectionError_ThenSucceeds()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueException(() => new HttpRequestException("boom"));
        handler.Respond(200, """{"version": "1.0.0"}""");
        using var client = NoBackoff(NewClient(handler));

        var version = await client.GetVersionAsync();

        Assert.Equal("1.0.0", version);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Post_TransportError_WrappedButNotRetried()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueException(() => new HttpRequestException("down"));
        using var client = NoBackoff(NewClient(handler));

        await Assert.ThrowsAsync<PagrConnectionException>(
            () => client.RenderAsync(TemplateId, """{"a": 1}"""));

        Assert.Equal(1, handler.CallCount);
    }

    // ── Retry-After / backoff ───────────────────────────────────────────────────────

    [Fact]
    public async Task RetryAfter_HonoredEvenPastBackoffMax()
    {
        // A Retry-After (here clamped down via a tiny RetryAfterMax, to keep the test fast)
        // larger than BackoffMax must still be honored via the Retry-After path, not silently
        // reduced to BackoffMax — i.e. the header value is measured against RetryAfterMax only.
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(503, "", headers: new() { ["Retry-After"] = "20" });
        handler.Respond(200, """{"version": "1.0.0"}""");
        using var client = NewClient(handler);
        client.Transport.BackoffMax = TimeSpan.FromMilliseconds(1);
        client.Transport.RetryAfterMax = TimeSpan.FromMilliseconds(5);

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var version = await client.GetVersionAsync();
        elapsed.Stop();

        Assert.Equal("1.0.0", version);
        Assert.Equal(2, handler.CallCount);
        // Clamped to RetryAfterMax (5ms), not BackoffMax (1ms) and not the raw 20s header value.
        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(4), $"elapsed was {elapsed.Elapsed}");
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1), $"elapsed was {elapsed.Elapsed}");
    }

    [Fact]
    public async Task RetryAfter_ClampedToRetryAfterMax()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(503, "", headers: new() { ["Retry-After"] = "9999" });
        handler.Respond(200, """{"version": "1.0.0"}""");
        using var client = NewClient(handler);
        client.Transport.RetryAfterMax = TimeSpan.FromMilliseconds(5);

        var version = await client.GetVersionAsync();

        Assert.Equal("1.0.0", version);
    }

    [Fact]
    public async Task Get_BackoffJitter_WhenRetryAfterAbsent_StaysWithinCeiling()
    {
        // No Retry-After header -> capped exponential backoff with full jitter: the single
        // retry's delay must land somewhere in [0, BackoffMax], never exceeding it and never
        // negative.
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(503, "");
        handler.Respond(200, """{"version": "1.0.0"}""");
        using var client = NewClient(handler);
        client.Transport.BackoffBase = TimeSpan.FromMilliseconds(20);
        client.Transport.BackoffMax = TimeSpan.FromMilliseconds(20);

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var version = await client.GetVersionAsync();
        elapsed.Stop();

        Assert.Equal("1.0.0", version);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1), $"elapsed was {elapsed.Elapsed}");
    }

    [Fact]
    public async Task RateLimitException_ExposesRetryAfter()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(429, """{"error": {"code": "RateLimit", "message": "slow"}}""", headers: new() { ["Retry-After"] = "42" });
        using var client = NoBackoff(NewClient(handler));

        var ex = await Assert.ThrowsAsync<PagrRateLimitException>(() => client.GetVersionAsync());

        Assert.Equal(TimeSpan.FromSeconds(42), ex.RetryAfter);
    }

    // ── Redirect following (HttpClient default; regression lock-in) ────────────────

    [Fact]
    public async Task GetAsync_FollowsRedirects()
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            var ctx1 = await listener.GetContextAsync();
            ctx1.Response.StatusCode = 302;
            ctx1.Response.RedirectLocation = $"http://127.0.0.1:{port}/v1/meta/version";
            ctx1.Response.Close();

            var ctx2 = await listener.GetContextAsync();
            var bytes = Encoding.UTF8.GetBytes("""{"version": "9.9.9"}""");
            ctx2.Response.ContentType = "application/json";
            ctx2.Response.ContentLength64 = bytes.Length;
            await ctx2.Response.OutputStream.WriteAsync(bytes);
            ctx2.Response.Close();
        });

        using var client = new PagrApiClient("key", $"http://127.0.0.1:{port}");
        var version = await client.GetVersionAsync();
        await serverTask;
        listener.Stop();

        Assert.Equal("9.9.9", version);
    }

    private static int GetFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
