using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Pagr.Sdk.Exceptions;

namespace Pagr.Sdk;

/// <summary>
/// Internal HTTP transport: request building, authentication, retry/backoff, timeouts, error
/// mapping and JSON serialisation. Shared by <see cref="PagrApiClient"/>.
/// </summary>
/// <remarks>
/// All client instances share a single static <see cref="SocketsHttpHandler"/> (with a
/// bounded <see cref="SocketsHttpHandler.PooledConnectionLifetime"/>) to pool connections
/// and pick up DNS changes, avoiding both socket exhaustion and stale connections. Each
/// transport owns a lightweight <see cref="HttpClient"/> over that shared handler so the
/// handler is never disposed when an individual client is.
/// </remarks>
internal sealed class PagrHttpClient : IDisposable
{
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// HTTP statuses worth retrying on an idempotent (GET) request: transient server/gateway
    /// failures (500/502/504) and a full render queue (503). 4xx statuses are deterministic
    /// and never retried — including 429: rate limiting reflects the caller's own request
    /// volume, so it is surfaced as <see cref="PagrRateLimitException"/> for the caller to
    /// handle, not retried silently.
    /// </summary>
    private static readonly HashSet<int> RetriableStatus = [500, 502, 503, 504];

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly TimeSpan _defaultTimeout;
    private readonly int _maxRetries;
    // volatile: SetApiKey can be called on a client shared across threads, so reads on
    // other threads must observe the swapped key without external synchronisation.
    private volatile string _apiKey;

    /// <summary>Test seam: the resolved (trailing-slash-trimmed) base URL.</summary>
    internal string BaseUrl => _baseUrl;

    /// <summary>Test seam: capped exponential backoff base, exposed so tests can zero it out.</summary>
    internal TimeSpan BackoffBase { get; set; } = TimeSpan.FromSeconds(0.5);

    /// <summary>Test seam: capped exponential backoff ceiling, exposed so tests can zero it out.</summary>
    internal TimeSpan BackoffMax { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Defensive upper bound on a server <c>Retry-After</c> value actually waited. The header
    /// is honored as-is up to this many seconds; larger (or hostile) values are clamped so a
    /// single retry can never park the caller for an unbounded time. Intentionally much larger
    /// than <see cref="BackoffMax"/> — this caps the server's explicit request, not our own
    /// computed backoff.
    /// </summary>
    internal TimeSpan RetryAfterMax { get; set; } = TimeSpan.FromSeconds(60);

    public PagrHttpClient(PagrClientOptions options)
        : this(options, SharedHandler, disposeHandler: false) { }

    /// <summary>Test seam: build a transport over a supplied handler (e.g. a stub).</summary>
    internal PagrHttpClient(PagrClientOptions options, HttpMessageHandler handler, bool disposeHandler = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            throw new ArgumentException("A base URL is required.", nameof(options));

        _baseUrl = options.BaseUrl.TrimEnd('/');
        _apiKey = options.ApiKey;
        _defaultTimeout = options.Timeout;
        _maxRetries = options.MaxRetries;
        // The shared HttpClient never enforces its own timeout: every request supplies its
        // own linked CancellationTokenSource (see SendAsync) so a per-request override can
        // differ from the client's default without racing two independent timeout sources.
        _http = new HttpClient(handler, disposeHandler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Replaces the API key used for subsequent requests.</summary>
    public void SetApiKey(string apiKey) => _apiKey = apiKey;

    /// <summary>
    /// Sends a GET request, retrying on transient failures, throwing a typed
    /// <see cref="PagrApiException"/> on error responses.
    /// </summary>
    public Task<HttpResponseMessage> GetAsync(
        string path,
        IReadOnlyCollection<KeyValuePair<string, string>>? query = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => SendAsync(
            HttpMethod.Get, path, query, content: null, retriable: true,
            headers: null, nonRaisingStatuses: null, timeout, cancellationToken);

    /// <summary>
    /// Serialises <paramref name="body"/> and sends it as a JSON POST. Never retried: the API
    /// has no idempotency keys, so a request that was applied but whose response was lost
    /// must not be repeated (it would render/charge twice).
    /// </summary>
    /// <param name="path">The request path, relative to the base URL.</param>
    /// <param name="body">The request body, serialised as JSON.</param>
    /// <param name="query">Query-string values; only non-null values are passed.</param>
    /// <param name="headers">
    /// Extra request headers. An <c>Accept</c> entry replaces the default
    /// <c>application/json</c> — that is how the raw-PDF render path opts into a binary
    /// response.
    /// </param>
    /// <param name="nonRaisingStatuses">
    /// Statuses that must be returned to the caller as a response instead of being mapped to
    /// a thrown <see cref="PagrApiException"/> — for endpoints where a 4xx carries a business
    /// outcome the caller has to read (e.g. render-PDF's 422 envelope).
    /// </param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public Task<HttpResponseMessage> PostAsync(
        string path, object body,
        IReadOnlyCollection<KeyValuePair<string, string>>? query = null,
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlySet<int>? nonRaisingStatuses = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => SendAsync(
            HttpMethod.Post, path, query, JsonContent(body), retriable: false,
            headers, nonRaisingStatuses, timeout, cancellationToken);

    /// <summary>Serialises <paramref name="body"/> and sends it as a JSON PATCH. Never retried.</summary>
    public Task<HttpResponseMessage> PatchAsync(
        string path, object body, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => SendAsync(
            HttpMethod.Patch, path, query: null, JsonContent(body), retriable: false,
            headers: null, nonRaisingStatuses: null, timeout, cancellationToken);

    private static StringContent JsonContent(object body)
    {
        var json = JsonSerializer.Serialize(body, SerializerOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    /// <summary>Appends the query string to the request path. Callers pass only non-null values.</summary>
    private string BuildUri(string path, IReadOnlyCollection<KeyValuePair<string, string>>? query)
    {
        var uri = $"{_baseUrl}/{path}";
        if (query is not { Count: > 0 })
            return uri;
        var pairs = query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}");
        return $"{uri}?{string.Join('&', pairs)}";
    }

    /// <summary>
    /// Builds a request. <c>Accept</c> defaults to <c>application/json</c> but is overridable
    /// through <paramref name="headers"/> (case-insensitively), so a caller can opt into a
    /// non-JSON response body without every other request growing a second Accept value.
    /// </summary>
    private HttpRequestMessage BuildRequest(
        HttpMethod method, string path, IReadOnlyCollection<KeyValuePair<string, string>>? query,
        string? bodyJson, IReadOnlyDictionary<string, string>? headers)
    {
        var request = new HttpRequestMessage(method, BuildUri(path, query));

        var accept = "application/json";
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                if (string.Equals(name, "Accept", StringComparison.OrdinalIgnoreCase))
                    accept = value;
                else
                    request.Headers.TryAddWithoutValidation(name, value);
            }
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        if (bodyJson is not null)
            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>
    /// Runs a request, applying the per-request timeout, wrapping transport failures into
    /// the <see cref="PagrApiException"/> tree, and retrying transient failures when
    /// <paramref name="retriable"/> (idempotent GET only).
    /// </summary>
    /// <remarks>
    /// The returned response is never disposed here on a non-throwing path — the caller owns
    /// it and must still be able to read both its headers and its body (the raw-PDF render
    /// path reads header metadata after consuming the body).
    /// </remarks>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, IReadOnlyCollection<KeyValuePair<string, string>>? query,
        HttpContent? content, bool retriable, IReadOnlyDictionary<string, string>? headers,
        IReadOnlySet<int>? nonRaisingStatuses, TimeSpan? timeoutOverride, CancellationToken cancellationToken)
    {
        // A body is only ever present for POST/PATCH, which are never retriable, so it is
        // safe to build the content once and reuse the same bytes across BuildRequest calls.
        var bodyJson = content is StringContent sc ? await sc.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : null;
        content?.Dispose();

        var maxAttempts = retriable ? _maxRetries + 1 : 1;
        var effectiveTimeout = timeoutOverride ?? _defaultTimeout;

        for (var attempt = 1; ; attempt++)
        {
            using var request = BuildRequest(method, path, query, bodyJson, headers);
            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Our own per-request timeout fired, not the caller's cancellation — a
                // caller-cancelled token propagates as a plain OperationCanceledException
                // instead of being caught here.
                if (retriable && attempt < maxAttempts)
                {
                    await BackoffAsync(attempt, retryAfter: null, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                throw new PagrTimeoutException("The request to the Pagr API timed out.");
            }
            catch (HttpRequestException ex)
            {
                if (retriable && attempt < maxAttempts)
                {
                    await BackoffAsync(attempt, retryAfter: null, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                throw new PagrConnectionException("Could not reach the Pagr API.", ex);
            }

            if (retriable && attempt < maxAttempts && RetriableStatus.Contains((int)response.StatusCode))
            {
                var retryAfter = ParseRetryAfter(response);
                response.Dispose();
                await BackoffAsync(attempt, retryAfter, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // A status the caller declared non-raising is handed back as-is: its body and
            // headers carry a business outcome the caller has to read, not an error.
            if (nonRaisingStatuses?.Contains((int)response.StatusCode) != true)
                await ThrowIfErrorAsync(response, cancellationToken).ConfigureAwait(false);
            return response;
        }
    }

    /// <summary>
    /// Sleeps before the next retry. When the server sends a <c>Retry-After</c> header
    /// carrying an integer number of seconds, that value is honored as-is — only clamped to
    /// <see cref="RetryAfterMax"/> (default 60s) as a defensive upper bound, never shortened
    /// below what the server asked for, even when it exceeds <see cref="BackoffMax"/>.
    /// Otherwise (no header, or a non-integer value such as an HTTP-date) it uses capped
    /// exponential backoff with full jitter.
    /// </summary>
    private async Task BackoffAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        TimeSpan delay;
        if (retryAfter is { } ra)
        {
            delay = ra > RetryAfterMax ? RetryAfterMax : ra;
        }
        else
        {
            var ceilingMs = Math.Min(BackoffBase.TotalMilliseconds * Math.Pow(2, attempt - 1), BackoffMax.TotalMilliseconds);
            delay = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * ceilingMs);
        }
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses a <c>Retry-After</c> header expressed as an integer number of seconds. Returns
    /// <see langword="null"/> when absent or not an integer (e.g. an HTTP-date), since the
    /// SDK does not interpret the date form.
    /// </summary>
    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values) &&
            int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }
        return null;
    }

    /// <summary>
    /// Inspects the response and throws the matching <see cref="PagrApiException"/> subclass
    /// for any 4xx/5xx status, reading the API's <c>{"error":{"code","message"}}</c> envelope.
    /// </summary>
    private static async Task ThrowIfErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var status = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var (code, message) = ParseError(body, fallbackMessage: $"Pagr API returned HTTP {status}.");

        if (status == 429)
        {
            var retryAfter = ParseRetryAfter(response);
            response.Dispose();
            throw new PagrRateLimitException(message, status, code, retryAfter);
        }
        response.Dispose();

        throw status switch
        {
            401 => new PagrAuthenticationException(message, status, code),
            403 => new PagrForbiddenException(message, status, code),
            404 => new PagrNotFoundException(message, status, code),
            413 => new PagrPayloadTooLargeException(message, status, code),
            422 => new PagrValidationFailedException(message, status, code),
            _ => new PagrGenericApiException(message, status, code),
        };
    }

    /// <summary>
    /// Extracts a <c>(code, message)</c> pair from an error body, reading the API's
    /// <c>{"error":{"code","message"}}</c> envelope and falling back to the raw body
    /// (or a generic message) when it is not the expected JSON.
    /// </summary>
    private static (string? Code, string Message) ParseError(string body, string fallbackMessage)
    {
        string? code = null;
        var message = string.IsNullOrWhiteSpace(body) ? fallbackMessage : body;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("error", out var error) &&
                    error.ValueKind == JsonValueKind.Object)
                {
                    if (error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String)
                        code = c.GetString();
                    if (error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                        message = m.GetString() ?? message;
                }
            }
            catch (JsonException)
            {
                // Not JSON; keep the raw body as the message.
            }
        }

        return (code, message);
    }

    public void Dispose() => _http.Dispose();
}
