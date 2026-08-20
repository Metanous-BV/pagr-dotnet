using System.Net.Http.Json;
using System.Text.Json;
using Pagr.Sdk.Exceptions;
using Pagr.Sdk.Models;

namespace Pagr.Sdk;

/// <summary>
/// Client for the Pagr document-rendering API (<c>/v1</c>).
/// </summary>
/// <remarks>
/// <para>
/// Provides methods for managing templates and versions, rendering documents
/// (synchronously, or via fire-and-forget jobs with webhook callbacks or
/// polling), validating data, browsing rendered documents and fonts, and retrieving
/// organisation statistics.
/// </para>
/// <para>
/// The client owns a pooled <see cref="HttpClient"/> internally. Create one and reuse it
/// for the lifetime of your application, then dispose it. Document data can be passed as a
/// JSON string, a <see cref="JsonElement"/>, or any serialisable object (POCO, anonymous
/// type, or <c>Dictionary&lt;string, object&gt;</c>) via the corresponding overload.
/// </para>
/// <para>
/// API error responses (4xx/5xx) are thrown as subclasses of <see cref="PagrApiException"/>.
/// Transport failures are wrapped too: a timeout raises <see cref="PagrTimeoutException"/> and
/// a connection/DNS failure raises <see cref="PagrConnectionException"/>, so callers only ever
/// catch <see cref="PagrApiException"/> subclasses. Business outcomes (failed validation,
/// insufficient credit, per-document render failures) are surfaced as data on the result
/// objects, never as exceptions.
/// </para>
/// <para>
/// Idempotent GET requests are retried on transient failures (HTTP 500/502/503/504, timeouts,
/// connection errors) with capped exponential backoff and jitter — see
/// <see cref="PagrClientOptions.MaxRetries"/>. Rate limits (429) are never retried: they
/// reflect the caller's own request volume, so <see cref="PagrRateLimitException"/> surfaces
/// for the caller to handle. Writes (POST/PATCH) are never retried either — the API has no
/// idempotency keys, so a request that was applied but whose response was lost must not be
/// repeated.
/// </para>
/// </remarks>
public sealed class PagrApiClient : IDisposable
{
    // Preserve caller-declared property names when serialising payloads, so a POCO/anonymous
    // object's keys reach the template exactly as written (matching the other SDKs).
    private static readonly JsonSerializerOptions PayloadOptions = new();

    /// <summary>Opts the render endpoint into streaming a raw PDF instead of the JSON envelope.</summary>
    private static readonly Dictionary<string, string> PdfAcceptHeaders =
        new() { ["Accept"] = "application/pdf" };

    /// <summary>
    /// A blocked raw-PDF render answers 422 with a JSON envelope instead of a PDF stream. That
    /// is a business outcome the caller reads off the result, so the transport must hand the
    /// response back rather than throwing <see cref="PagrValidationFailedException"/>.
    /// </summary>
    private static readonly HashSet<int> PdfNonRaisingStatuses = [422];

    /// <summary>
    /// The default overall deadline for <see cref="WaitForJobAsync"/>: 5 minutes. A render job
    /// that never reaches a terminal state (a stuck server, a lost webhook, a bug) must not
    /// hang the caller forever by default. Pass <see cref="Timeout.InfiniteTimeSpan"/> as the
    /// <c>timeout</c> argument explicitly to opt back into unbounded polling.
    /// </summary>
    public static readonly TimeSpan DefaultWaitForJobTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Test seam: overrides <see cref="DefaultWaitForJobTimeout"/> for this client instance, so
    /// tests can exercise the "no explicit <c>timeout</c> passed" default path without waiting
    /// the real 5 minutes for it to elapse.
    /// </summary>
    internal TimeSpan? WaitForJobDefaultTimeoutOverride { get; set; }

    private readonly PagrHttpClient _http;

    /// <summary>
    /// The default base URL of the hosted Pagr Public API, used when the caller does not pass
    /// an explicit <c>baseUrl</c> (e.g. to target a local dev instance).
    /// </summary>
    public const string DefaultBaseUrl = "https://api.pagr.eu";

    /// <summary>
    /// Initialises a new <see cref="PagrApiClient"/>.
    /// </summary>
    /// <param name="apiKey">The organisation API key, sent as a bearer token on every request.</param>
    /// <param name="baseUrl">
    /// Base URL of the Pagr API, e.g. <c>https://api.pagr.eu</c>. Defaults to the hosted
    /// production API (<see cref="DefaultBaseUrl"/>); pass this only to target another
    /// instance, e.g. a local dev server.
    /// </param>
    /// <param name="options">Optional settings (e.g. <see cref="PagrClientOptions.Timeout"/>, <see cref="PagrClientOptions.MaxRetries"/>).</param>
    public PagrApiClient(string apiKey, string? baseUrl = null, PagrClientOptions? options = null)
    {
        var effective = new PagrClientOptions
        {
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl,
            ApiKey = apiKey,
            Timeout = options?.Timeout ?? PagrClientOptions.DefaultTimeout,
            MaxRetries = options?.MaxRetries ?? PagrClientOptions.DefaultMaxRetries,
        };
        _http = new PagrHttpClient(effective);
    }

    /// <summary>Test seam: build a client over a supplied <see cref="HttpMessageHandler"/> (e.g. a stub).</summary>
    internal PagrApiClient(PagrClientOptions options, HttpMessageHandler handler)
    {
        _http = new PagrHttpClient(options, handler);
    }

    /// <summary>Test seam: reach the underlying transport (e.g. to zero out backoff delays).</summary>
    internal PagrHttpClient Transport => _http;

    /// <summary>Replaces the API key used for subsequent requests.</summary>
    /// <param name="apiKey">The new API key.</param>
    public void SetApiKey(string apiKey) => _http.SetApiKey(apiKey);

    // ── Templates ────────────────────────────────────────────────────────────────

    /// <summary>Lists templates available to the authenticated organisation.</summary>
    /// <param name="options">Paging, sorting, filtering and search options.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A page of templates; use <see cref="PagedResult{T}.Items"/> and <see cref="PagedResult{T}.Total"/>.</returns>
    /// <exception cref="ArgumentException">A filter's field or operator is not valid for this endpoint.</exception>
    public async Task<PagedResult<Template>> GetTemplatesAsync(
        ListOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("v1/templates", options?.ToQuery(Filters.TemplateFilters), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await ReadJsonAsync<PagedResult<Template>>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the templates in a specific project.</summary>
    /// <param name="projectId">The project to list templates for.</param>
    /// <param name="options">Paging, sorting, filtering and search options.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A page of the project's templates.</returns>
    /// <exception cref="ArgumentException">A filter's field or operator is not valid for this endpoint.</exception>
    public async Task<PagedResult<Template>> GetTemplatesAsync(
        Guid projectId, ListOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync(
                $"v1/projects/{projectId}/templates", options?.ToQuery(Filters.TemplateFilters), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await ReadJsonAsync<PagedResult<Template>>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches a single template by ID.</summary>
    /// <param name="templateId">The template to fetch.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The template.</returns>
    public async Task<Template> GetTemplateAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"v1/templates/{templateId}", query: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await ReadJsonAsync<Template>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the versions of a template.</summary>
    /// <param name="templateId">The template to list versions for.</param>
    /// <param name="options">Paging, sorting, filtering and search options.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A page of template versions.</returns>
    /// <exception cref="ArgumentException">A filter's field or operator is not valid for this endpoint.</exception>
    public async Task<PagedResult<TemplateVersion>> GetTemplateVersionsAsync(
        Guid templateId, ListOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync(
                $"v1/templates/{templateId}/versions", options?.ToQuery(Filters.TemplateVersionFilters), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await ReadJsonAsync<PagedResult<TemplateVersion>>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches a specific template version, or the latest published one.</summary>
    /// <param name="templateId">The template.</param>
    /// <param name="version">A version number, or <see langword="null"/> (default) for the latest published version.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The requested template version.</returns>
    public async Task<TemplateVersion> GetTemplateVersionAsync(
        Guid templateId, int? version = null, CancellationToken cancellationToken = default)
    {
        var suffix = version?.ToString() ?? "latest";
        var response = await _http.GetAsync(
                $"v1/templates/{templateId}/versions/{suffix}", query: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await ReadJsonAsync<TemplateVersion>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates a version's document-name template.</summary>
    /// <param name="templateId">The template.</param>
    /// <param name="versionNumber">The version to update.</param>
    /// <param name="documentNameTemplate">The new name template, or <see langword="null"/> to clear it.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The updated version.</returns>
    public async Task<TemplateVersion> UpdateDocumentNameTemplateAsync(
        Guid templateId, int versionNumber, string? documentNameTemplate,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.PatchAsync(
                $"v1/templates/{templateId}/versions/{versionNumber}/document-name-template",
                new { documentNameTemplate }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await ReadJsonAsync<TemplateVersion>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the URL of a version's preview image, if any.</summary>
    /// <param name="templateId">The template.</param>
    /// <param name="versionNumber">The version to fetch the preview image for.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The preview image URL, or <see langword="null"/> when the version has none.</returns>
    public async Task<string?> GetPreviewImageUrlAsync(
        Guid templateId, int versionNumber, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync(
                $"v1/templates/{templateId}/versions/{versionNumber}/preview-image", query: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await ReadStringPropertyAsync(response, "url", cancellationToken).ConfigureAwait(false);
    }

    // ── Render ───────────────────────────────────────────────────────────────────

    /// <summary>Renders a single document.</summary>
    /// <param name="templateId">The template to render.</param>
    /// <param name="jsonData">The document data as a JSON string.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> (default) for the latest published version.</param>
    /// <param name="includeDocument">Whether to return the rendered bytes inline (Base64).</param>
    /// <param name="language">Language variant to render (for multilingual templates).</param>
    /// <param name="persist">
    /// When <see langword="false"/> the render is not stored server-side; the document's
    /// <see cref="RenderedDocument.Id"/> and <see cref="RenderedDocument.ViewUrl"/> are then
    /// <see langword="null"/>. To receive the PDF as a raw byte stream instead of a Base64
    /// field, use <see cref="RenderPdfAsync(Guid, string, int?, string?, bool, TimeSpan?, CancellationToken)"/>.
    /// </param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's configured default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The render result for the single document.</returns>
    /// <exception cref="System.Text.Json.JsonException">Thrown when <paramref name="jsonData"/> is not valid JSON.</exception>
    public Task<RenderResult> RenderAsync(
        Guid templateId, string jsonData, int? version = null, bool includeDocument = false,
        string? language = null, bool persist = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => RenderCoreAsync(templateId, [ToPayload(jsonData)], version, includeDocument, language, persist, timeout, cancellationToken);

    /// <summary>Renders a single document.</summary>
    /// <param name="templateId">The template to render.</param>
    /// <param name="data">The document data.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> (default) for the latest published version.</param>
    /// <param name="includeDocument">Whether to return the rendered bytes inline (Base64).</param>
    /// <param name="language">Language variant to render (for multilingual templates).</param>
    /// <param name="persist">When <see langword="false"/> the render is not stored server-side.</param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's configured default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The render result for the single document.</returns>
    public Task<RenderResult> RenderAsync(
        Guid templateId, JsonElement data, int? version = null, bool includeDocument = false,
        string? language = null, bool persist = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => RenderCoreAsync(templateId, [data], version, includeDocument, language, persist, timeout, cancellationToken);

    /// <summary>Renders a single document from any serialisable object (POCO, anonymous type, dictionary).</summary>
    /// <typeparam name="T">The document data type; property names are preserved exactly as declared.</typeparam>
    /// <param name="templateId">The template to render.</param>
    /// <param name="data">The document data.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> (default) for the latest published version.</param>
    /// <param name="includeDocument">Whether to return the rendered bytes inline (Base64).</param>
    /// <param name="language">Language variant to render (for multilingual templates).</param>
    /// <param name="persist">When <see langword="false"/> the render is not stored server-side.</param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's configured default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The render result for the single document.</returns>
    public Task<RenderResult> RenderAsync<T>(
        Guid templateId, T data, int? version = null, bool includeDocument = false,
        string? language = null, bool persist = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => RenderCoreAsync(templateId, [ToPayload(data)], version, includeDocument, language, persist, timeout, cancellationToken);

    /// <summary>Renders a single document and streams the raw PDF back.</summary>
    /// <remarks>
    /// <para>
    /// The opt-in <c>Accept: application/pdf</c> path: instead of the JSON envelope
    /// <c>RenderAsync</c> returns, the API streams the PDF binary directly and carries the
    /// document metadata in <c>X-Pagr-*</c> response headers. Use it when you want the bytes
    /// without Base64-decoding a JSON field.
    /// </para>
    /// <para>
    /// Only single-document renders are supported — this always sends exactly one document.
    /// (The API rejects a raw-PDF request for a batch with HTTP 406, surfaced as
    /// <see cref="PagrGenericApiException"/>.)
    /// </para>
    /// <para>
    /// A blocked or failed render has no PDF to stream, so the API answers with HTTP 422 and a
    /// JSON envelope. That is a business outcome, not an exception: the result's
    /// <see cref="PdfRenderResult.Ok"/> is <see langword="false"/> and the reasons are in
    /// <see cref="PdfRenderResult.Issues"/> / <see cref="PdfRenderResult.Status"/>.
    /// </para>
    /// </remarks>
    /// <param name="templateId">The template to render.</param>
    /// <param name="jsonData">The document data as a JSON string.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> (default) for the latest published version.</param>
    /// <param name="language">Language variant to render (for multilingual templates).</param>
    /// <param name="persist">
    /// When <see langword="true"/> (default) the render is stored server-side and the
    /// document's <see cref="PdfDocument.DocumentId"/>/<see cref="PdfDocument.ViewUrl"/> are
    /// populated; when <see langword="false"/> nothing is stored and both are <see langword="null"/>.
    /// </param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's configured default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The PDF render result for the single document.</returns>
    /// <exception cref="System.Text.Json.JsonException">Thrown when <paramref name="jsonData"/> is not valid JSON.</exception>
    public Task<PdfRenderResult> RenderPdfAsync(
        Guid templateId, string jsonData, int? version = null,
        string? language = null, bool persist = true, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => RenderPdfCoreAsync(templateId, ToPayload(jsonData), version, language, persist, timeout, cancellationToken);

    /// <summary>Renders a single document and streams the raw PDF back.</summary>
    /// <remarks>See <see cref="RenderPdfAsync(Guid, string, int?, string?, bool, TimeSpan?, CancellationToken)"/>.</remarks>
    /// <param name="templateId">The template to render.</param>
    /// <param name="data">The document data.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> (default) for the latest published version.</param>
    /// <param name="language">Language variant to render (for multilingual templates).</param>
    /// <param name="persist">When <see langword="false"/> the render is not stored server-side.</param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's configured default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The PDF render result for the single document.</returns>
    public Task<PdfRenderResult> RenderPdfAsync(
        Guid templateId, JsonElement data, int? version = null,
        string? language = null, bool persist = true, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => RenderPdfCoreAsync(templateId, data, version, language, persist, timeout, cancellationToken);

    /// <summary>
    /// Renders a single document from any serialisable object (POCO, anonymous type,
    /// dictionary) and streams the raw PDF back.
    /// </summary>
    /// <remarks>See <see cref="RenderPdfAsync(Guid, string, int?, string?, bool, TimeSpan?, CancellationToken)"/>.</remarks>
    /// <typeparam name="T">The document data type; property names are preserved exactly as declared.</typeparam>
    /// <param name="templateId">The template to render.</param>
    /// <param name="data">The document data.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> (default) for the latest published version.</param>
    /// <param name="language">Language variant to render (for multilingual templates).</param>
    /// <param name="persist">When <see langword="false"/> the render is not stored server-side.</param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's configured default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The PDF render result for the single document.</returns>
    public Task<PdfRenderResult> RenderPdfAsync<T>(
        Guid templateId, T data, int? version = null,
        string? language = null, bool persist = true, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => RenderPdfCoreAsync(templateId, ToPayload(data), version, language, persist, timeout, cancellationToken);

    /// <summary>Renders multiple documents in a single request.</summary>
    /// <param name="templateId">The template to render.</param>
    /// <param name="jsonDataSets">The document data sets, each a JSON string.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> for the latest.</param>
    /// <param name="includeDocument">Whether to return the rendered bytes inline (Base64).</param>
    /// <param name="language">Language variant to render.</param>
    /// <param name="persist">When <see langword="false"/> the renders are not stored server-side.</param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's configured default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>
/// A result correlating each submitted input to its rendered document — matched via the
/// <see cref="RenderedDocument.DocumentIndex"/> the API reports — or the issues that prevented it.
/// </returns>
    /// <exception cref="System.Text.Json.JsonException">Thrown when any entry of <paramref name="jsonDataSets"/> is not valid JSON.</exception>
    public Task<BatchRenderResult> RenderBatchAsync(
        Guid templateId, IEnumerable<string> jsonDataSets, int? version = null, bool includeDocument = false,
        string? language = null, bool persist = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => RenderBatchCoreAsync(templateId, jsonDataSets.Select(ToPayload).ToList(), version, includeDocument, language, persist, timeout, cancellationToken);

    /// <summary>Renders multiple documents in a single request.</summary>
    /// <param name="templateId">The template to render.</param>
    /// <param name="dataSets">The document data sets.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> for the latest.</param>
    /// <param name="includeDocument">Whether to return the rendered bytes inline (Base64).</param>
    /// <param name="language">Language variant to render.</param>
    /// <param name="persist">When <see langword="false"/> the renders are not stored server-side.</param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's configured default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>
/// A result correlating each submitted input to its rendered document — matched via the
/// <see cref="RenderedDocument.DocumentIndex"/> the API reports — or the issues that prevented it.
/// </returns>
    public Task<BatchRenderResult> RenderBatchAsync(
        Guid templateId, IEnumerable<JsonElement> dataSets, int? version = null, bool includeDocument = false,
        string? language = null, bool persist = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => RenderBatchCoreAsync(templateId, dataSets.ToList(), version, includeDocument, language, persist, timeout, cancellationToken);

    /// <summary>Renders multiple documents from serialisable objects (POCOs, anonymous types, dictionaries).</summary>
    /// <typeparam name="T">The document data type; property names are preserved exactly as declared.</typeparam>
    /// <param name="templateId">The template to render.</param>
    /// <param name="dataSets">The document data sets.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> for the latest.</param>
    /// <param name="includeDocument">Whether to return the rendered bytes inline (Base64).</param>
    /// <param name="language">Language variant to render.</param>
    /// <param name="persist">When <see langword="false"/> the renders are not stored server-side.</param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's configured default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>
/// A result correlating each submitted input to its rendered document — matched via the
/// <see cref="RenderedDocument.DocumentIndex"/> the API reports — or the issues that prevented it.
/// </returns>
    public Task<BatchRenderResult> RenderBatchAsync<T>(
        Guid templateId, IEnumerable<T> dataSets, int? version = null, bool includeDocument = false,
        string? language = null, bool persist = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => RenderBatchCoreAsync(templateId, dataSets.Select(d => ToPayload(d)).ToList(), version, includeDocument, language, persist, timeout, cancellationToken);

    /// <summary>Enqueues a fire-and-forget batch render.</summary>
    /// <remarks>
    /// Returns immediately with a job reference. The Pagr server then renders in the
    /// background and POSTs progress and completion webhooks to <paramref name="callbackUrl"/>;
    /// use <see cref="Webhooks.RenderCallback.Parse(string)"/> to parse them. You can also
    /// poll <see cref="GetJobStatusAsync"/> or <see cref="WaitForJobAsync"/>.
    /// </remarks>
    /// <param name="templateId">The template to render.</param>
    /// <param name="jsonDataSets">The document data sets, each a JSON string.</param>
    /// <param name="callbackUrl">A publicly reachable URL the server POSTs webhooks to.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> for the latest.</param>
    /// <param name="includeDocument">Whether webhooks include the rendered bytes inline.</param>
    /// <param name="language">Language variant to render.</param>
    /// <param name="persist">Whether the renders are stored server-side.</param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's configured default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A reference to the enqueued job (state <see cref="RenderJobState.Queued"/>).</returns>
    /// <exception cref="System.Text.Json.JsonException">Thrown when any entry of <paramref name="jsonDataSets"/> is not valid JSON.</exception>
    public Task<RenderJob> EnqueueBatchRenderAsync(
        Guid templateId, IEnumerable<string> jsonDataSets, string callbackUrl, int? version = null,
        bool includeDocument = false, string? language = null, bool persist = true,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(templateId, jsonDataSets.Select(ToPayload).ToList(), callbackUrl, version, includeDocument, language, persist, timeout, cancellationToken);

    /// <summary>Enqueues a fire-and-forget batch render.</summary>
    /// <param name="templateId">The template to render.</param>
    /// <param name="dataSets">The document data sets.</param>
    /// <param name="callbackUrl">A publicly reachable URL the server POSTs webhooks to.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> for the latest.</param>
    /// <param name="includeDocument">Whether webhooks include the rendered bytes inline.</param>
    /// <param name="language">Language variant to render.</param>
    /// <param name="persist">Whether the renders are stored server-side.</param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's configured default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A reference to the enqueued job (state <see cref="RenderJobState.Queued"/>).</returns>
    public Task<RenderJob> EnqueueBatchRenderAsync(
        Guid templateId, IEnumerable<JsonElement> dataSets, string callbackUrl, int? version = null,
        bool includeDocument = false, string? language = null, bool persist = true,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(templateId, dataSets.ToList(), callbackUrl, version, includeDocument, language, persist, timeout, cancellationToken);

    /// <summary>Enqueues a fire-and-forget batch render from serialisable objects.</summary>
    /// <typeparam name="T">The document data type; property names are preserved exactly as declared.</typeparam>
    /// <param name="templateId">The template to render.</param>
    /// <param name="dataSets">The document data sets.</param>
    /// <param name="callbackUrl">A publicly reachable URL the server POSTs webhooks to.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> for the latest.</param>
    /// <param name="includeDocument">Whether webhooks include the rendered bytes inline.</param>
    /// <param name="language">Language variant to render.</param>
    /// <param name="persist">Whether the renders are stored server-side.</param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's configured default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A reference to the enqueued job (state <see cref="RenderJobState.Queued"/>).</returns>
    public Task<RenderJob> EnqueueBatchRenderAsync<T>(
        Guid templateId, IEnumerable<T> dataSets, string callbackUrl, int? version = null,
        bool includeDocument = false, string? language = null, bool persist = true,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(templateId, dataSets.Select(d => ToPayload(d)).ToList(), callbackUrl, version, includeDocument, language, persist, timeout, cancellationToken);

    /// <summary>Polls the status of an async render job.</summary>
    /// <remarks>
    /// A reliable alternative to the webhook callback: returns the job's lifecycle state
    /// and outcome, and how many documents it has produced.
    /// </remarks>
    /// <param name="jobId">The <see cref="RenderJob.JobId"/> returned by <c>EnqueueBatchRenderAsync</c>.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The job's current status; poll until <see cref="RenderJobStatus.Done"/> (or use <see cref="WaitForJobAsync"/>).</returns>
    public async Task<RenderJobStatus> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"v1/render/jobs/{jobId}", query: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await ReadJsonAsync<RenderJobStatus>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Polls <see cref="GetJobStatusAsync"/> until the job reaches a terminal state.</summary>
    /// <remarks>
    /// A convenience wrapper over a hand-rolled polling loop. Because
    /// <see cref="RenderJobStatus.Done"/> treats an unrecognised state as terminal
    /// (fail-open), this never spins forever on a server state the SDK does not know about.
    /// </remarks>
    /// <param name="jobId">The job returned by <c>EnqueueBatchRenderAsync</c>.</param>
    /// <param name="pollInterval">Time to wait between status polls. Defaults to 2 seconds.</param>
    /// <param name="timeout">
    /// Overall deadline across all polls. <see langword="null"/> (default) uses
    /// <see cref="DefaultWaitForJobTimeout"/> (5 minutes) so a job that never reaches a
    /// terminal state cannot hang the caller forever. Pass <see cref="Timeout.InfiniteTimeSpan"/>
    /// explicitly to poll with no deadline.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The terminal <see cref="RenderJobStatus"/> (its <see cref="RenderJobStatus.State"/> is <see cref="RenderJobState.Completed"/>, <see cref="RenderJobState.Failed"/>, or <see cref="RenderJobState.Unknown"/>).</returns>
    /// <exception cref="PagrTimeoutException">Thrown if <paramref name="timeout"/> elapses before the job finishes.</exception>
    public async Task<RenderJobStatus> WaitForJobAsync(
        Guid jobId, TimeSpan? pollInterval = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var interval = pollInterval ?? TimeSpan.FromSeconds(2);
        var effectiveTimeout = timeout ?? WaitForJobDefaultTimeoutOverride ?? DefaultWaitForJobTimeout;
        var stopwatch = effectiveTimeout != Timeout.InfiniteTimeSpan ? System.Diagnostics.Stopwatch.StartNew() : null;

        while (true)
        {
            var status = await GetJobStatusAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (status.Done)
                return status;

            if (stopwatch is not null)
            {
                var remaining = effectiveTimeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    throw new PagrTimeoutException($"Job {jobId} did not finish within {effectiveTimeout}.");
                await Task.Delay(remaining < interval ? remaining : interval, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // ── Validate ─────────────────────────────────────────────────────────────────

    /// <summary>Validates document data against a template without rendering.</summary>
    /// <param name="templateId">The template to validate against.</param>
    /// <param name="jsonData">
    /// A single document as a JSON string. A JSON string encoding an array is treated as a
    /// batch, one document per element.
    /// </param>
    /// <param name="version">A specific version number, or <see langword="null"/> for the latest.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The validation results; check <see cref="ValidationResponse.IsValid"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">Thrown when <paramref name="jsonData"/> is not valid JSON.</exception>
    public Task<ValidationResponse> ValidateAsync(
        Guid templateId, string jsonData, int? version = null, CancellationToken cancellationToken = default)
        => ValidateCoreAsync(templateId, ExpandDocuments(ToPayload(jsonData)), version, cancellationToken);

    /// <summary>Validates document data against a template without rendering.</summary>
    /// <param name="templateId">The template to validate against.</param>
    /// <param name="data">A single document. A JSON array is treated as a batch, one document per element.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> for the latest.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The validation results; check <see cref="ValidationResponse.IsValid"/>.</returns>
    public Task<ValidationResponse> ValidateAsync(
        Guid templateId, JsonElement data, int? version = null, CancellationToken cancellationToken = default)
        => ValidateCoreAsync(templateId, ExpandDocuments(data), version, cancellationToken);

    /// <summary>Validates a serialisable object (POCO, anonymous type, dictionary) against a template.</summary>
    /// <typeparam name="T">The document data type; property names are preserved exactly as declared.</typeparam>
    /// <param name="templateId">The template to validate against.</param>
    /// <param name="data">A single document. A value serialising to a JSON array is treated as a batch.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> for the latest.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The validation results; check <see cref="ValidationResponse.IsValid"/>.</returns>
    public Task<ValidationResponse> ValidateAsync<T>(
        Guid templateId, T data, int? version = null, CancellationToken cancellationToken = default)
        => ValidateCoreAsync(templateId, ExpandDocuments(ToPayload(data)), version, cancellationToken);

    /// <summary>Validates multiple documents against a template without rendering.</summary>
    /// <param name="templateId">The template to validate against.</param>
    /// <param name="jsonDataSets">The document data sets, each a JSON string.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> for the latest.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The validation results; check <see cref="ValidationResponse.IsValid"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">Thrown when any entry of <paramref name="jsonDataSets"/> is not valid JSON.</exception>
    public Task<ValidationResponse> ValidateAsync(
        Guid templateId, IEnumerable<string> jsonDataSets, int? version = null,
        CancellationToken cancellationToken = default)
        => ValidateCoreAsync(templateId, jsonDataSets.Select(ToPayload).ToList(), version, cancellationToken);

    /// <summary>Validates multiple documents against a template without rendering.</summary>
    /// <param name="templateId">The template to validate against.</param>
    /// <param name="dataSets">The document data sets.</param>
    /// <param name="version">A specific version number, or <see langword="null"/> for the latest.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The validation results; check <see cref="ValidationResponse.IsValid"/>.</returns>
    public Task<ValidationResponse> ValidateAsync(
        Guid templateId, IEnumerable<JsonElement> dataSets, int? version = null,
        CancellationToken cancellationToken = default)
        => ValidateCoreAsync(templateId, dataSets.ToList(), version, cancellationToken);

    // ── Documents ────────────────────────────────────────────────────────────────

    /// <summary>Lists rendered documents for the authenticated organisation.</summary>
    /// <param name="options">Paging, sorting, filtering and search options.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A page of rendered documents.</returns>
    /// <exception cref="ArgumentException">A filter's field or operator is not valid for this endpoint.</exception>
    public async Task<PagedResult<RenderDocument>> GetDocumentsAsync(
        ListOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("v1/documents", options?.ToQuery(Filters.DocumentFilters), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await ReadJsonAsync<PagedResult<RenderDocument>>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches a single rendered document's metadata by ID.</summary>
    /// <param name="documentId">The document to fetch.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The document's metadata.</returns>
    public async Task<RenderDocument> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"v1/documents/{documentId}", query: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await ReadJsonAsync<RenderDocument>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Downloads a rendered document's PDF bytes.</summary>
    /// <param name="documentId">The document to download.</param>
    /// <param name="timeout">Per-request timeout override; <see langword="null"/> uses the client's configured default.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The document's PDF bytes.</returns>
    public async Task<byte[]> DownloadDocumentAsync(
        Guid documentId, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync(
                $"v1/documents/{documentId}/file", query: null, timeout: timeout, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (response)
        {
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // ── Fonts ────────────────────────────────────────────────────────────────────

    /// <summary>Lists the font family names available for rendering.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The available font family names.</returns>
    public async Task<IReadOnlyList<string>> GetFontsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("v1/fonts", query: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<List<string>>(response, cancellationToken).ConfigureAwait(false);
    }

    // ── Organisation ─────────────────────────────────────────────────────────────

    /// <summary>Fetches usage and credit statistics for the authenticated organisation.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The organisation's current usage statistics.</returns>
    public async Task<OrgStats> GetOrgStatsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("v1/organisation/stats", query: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await ReadJsonAsync<OrgStats>(response, cancellationToken).ConfigureAwait(false);
    }

    // ── Meta ─────────────────────────────────────────────────────────────────────

    /// <summary>Checks API health.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns><see langword="true"/> when the service reports healthy; otherwise a <see cref="PagrApiException"/> (503) is thrown.</returns>
    public async Task<bool> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("v1/meta/status", query: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        response.Dispose();
        return true;
    }

    /// <summary>Returns the deployed API version string.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The API version, or <see langword="null"/> when the API does not report one.</returns>
    public async Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("v1/meta/version", query: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ReadStringPropertyAsync(response, "version", cancellationToken).ConfigureAwait(false);
    }

    // ── Core request helpers ─────────────────────────────────────────────────────

    private async Task<RenderResult> RenderCoreAsync(
        Guid templateId, IReadOnlyList<JsonElement> documents, int? version, bool includeDocument,
        string? language, bool persist, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        // This path always receives the JSON result envelope — including with persist=false,
        // where the API returns the same envelope with a null id/viewUrl. Only
        // RenderPdfAsync sends Accept: application/pdf, so there is no content type to sniff.
        var body = new { documents, includeDocument };
        var response = await _http.PostAsync(
                RenderPath(templateId, version), body, RenderQuery(language, persist),
                timeout: timeout, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var api = await ReadJsonAsync<RenderApiResponse>(response, cancellationToken).ConfigureAwait(false);
        return RenderResult.FromApi(api);
    }

    private async Task<PdfRenderResult> RenderPdfCoreAsync(
        Guid templateId, JsonElement document, int? version, string? language, bool persist,
        TimeSpan? timeout, CancellationToken cancellationToken)
    {
        // The body is always a one-element array, and carries no includeDocument flag: that
        // only applies to the JSON-envelope path, where the bytes are an optional field.
        var body = new { documents = new[] { document } };
        var response = await _http.PostAsync(
                RenderPath(templateId, version), body, RenderQuery(language, persist),
                headers: PdfAcceptHeaders, nonRaisingStatuses: PdfNonRaisingStatuses,
                timeout: timeout, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Dispose only after both the body and the headers have been read: PdfDocument's
        // metadata lives in the response headers, not in the payload.
        using (response)
        {
            if ((int)response.StatusCode == 422)
            {
                var envelope = await PagrJson.ReadElementAsync(response.Content, 422, cancellationToken)
                    .ConfigureAwait(false);
                return PdfRenderResult.FromErrorEnvelope(envelope);
            }

            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return new PdfRenderResult
            {
                Document = PdfDocument.FromResponse(response, content),
                Status = "ok",
            };
        }
    }

    private async Task<BatchRenderResult> RenderBatchCoreAsync(
        Guid templateId, IReadOnlyList<JsonElement> inputs, int? version, bool includeDocument,
        string? language, bool persist, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var body = new { documents = inputs, includeDocument };
        var response = await _http.PostAsync(
                RenderPath(templateId, version), body, RenderQuery(language, persist),
                timeout: timeout, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var api = await ReadJsonAsync<RenderApiResponse>(response, cancellationToken).ConfigureAwait(false);
        return BatchRenderResult.FromApi(api, inputs);
    }

    private async Task<RenderJob> EnqueueCoreAsync(
        Guid templateId, IReadOnlyList<JsonElement> documents, string callbackUrl, int? version,
        bool includeDocument, string? language, bool persist, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var body = new { documents, callbackUrl, includeDocument };
        var response = await _http.PostAsync(
                RenderPath(templateId, version, "/async"), body, RenderQuery(language, persist),
                timeout: timeout, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await ReadJsonAsync<RenderJob>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ValidationResponse> ValidateCoreAsync(
        Guid templateId, IReadOnlyList<JsonElement> documents, int? version, CancellationToken cancellationToken)
    {
        var body = new { documents };
        var response = await _http.PostAsync(
                RenderPath(templateId, version, "/validate"), body, query: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var api = await ReadJsonAsync<ValidationApiResponse>(response, cancellationToken).ConfigureAwait(false);
        return ValidationResponse.FromApi(api);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a render endpoint path. <paramref name="version"/> <see langword="null"/>
    /// targets the latest published version; otherwise the specific version.
    /// </summary>
    private static string RenderPath(Guid templateId, int? version, string suffix = "")
    {
        var basePath = version is null
            ? $"v1/render/{templateId}"
            : $"v1/render/{templateId}/versions/{version}";
        return basePath + suffix;
    }

    /// <summary>
    /// Builds the render query: <c>language</c> is sent only when set; <c>persist</c> is
    /// always sent, as a lowercase boolean (the wire form shared by all Pagr SDKs).
    /// </summary>
    private static IReadOnlyCollection<KeyValuePair<string, string>> RenderQuery(string? language, bool persist)
    {
        var query = new List<KeyValuePair<string, string>>(2);
        if (language is not null)
            query.Add(new("language", language));
        query.Add(new("persist", persist ? "true" : "false"));
        return query;
    }

    /// <summary>Parses a caller-supplied JSON string into a payload element.</summary>
    private static JsonElement ToPayload(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    /// <summary>
    /// Serialises a caller-supplied document into a payload element, preserving declared
    /// property names so a POCO/anonymous object's keys reach the template exactly as written.
    /// </summary>
    private static JsonElement ToPayload<T>(T data) => data switch
    {
        JsonElement element => element,
        string json => ToPayload(json),
        _ => JsonSerializer.SerializeToElement(data, PayloadOptions),
    };

    /// <summary>
    /// A JSON array payload is a batch (one document per element); anything else is a
    /// single document. Array elements that are themselves JSON-encoded strings are
    /// parsed into documents, mirroring the string overloads.
    /// </summary>
    private static List<JsonElement> ExpandDocuments(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Array
            ? payload.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? ToPayload(e.GetString()!) : e)
                .ToList()
            : [payload];

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            return await PagrJson.ReadAsync<T>(response.Content, (int)response.StatusCode, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reads a single optional string property (e.g. <c>url</c>, <c>version</c>) from a JSON object response.</summary>
    private static async Task<string?> ReadStringPropertyAsync(
        HttpResponseMessage response, string propertyName, CancellationToken cancellationToken)
    {
        using (response)
        {
            JsonDocument doc;
            try
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new PagrDecodeException(
                    "The Pagr API returned a response that could not be decoded as JSON.",
                    (int)response.StatusCode, ex);
            }
            using (doc)
            {
                return doc.RootElement.ValueKind == JsonValueKind.Object &&
                       doc.RootElement.TryGetProperty(propertyName, out var value) &&
                       value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
            }
        }
    }

    /// <summary>Disposes the internally pooled <see cref="HttpClient"/>.</summary>
    public void Dispose() => _http.Dispose();
}
