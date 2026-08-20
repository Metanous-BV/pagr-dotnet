namespace Pagr.Sdk;

/// <summary>
/// Optional settings for a <see cref="PagrApiClient"/>. The base URL and API key are
/// constructor arguments; this object carries the remaining tunables (e.g. <see cref="Timeout"/>).
/// </summary>
public sealed class PagrClientOptions
{
    /// <summary>The default per-request timeout (30 seconds), matching the other Pagr SDKs.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The default maximum retries for transient failures on idempotent (GET) requests.</summary>
    public const int DefaultMaxRetries = 2;

    /// <summary>
    /// Base URL of the Pagr API. Set internally from the <see cref="PagrApiClient"/> constructor
    /// argument; not part of the public options surface.
    /// </summary>
    internal string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The organisation API key. Set internally from the <see cref="PagrApiClient"/> constructor
    /// argument; not part of the public options surface.
    /// </summary>
    internal string ApiKey { get; set; } = string.Empty;

    /// <summary>Per-request timeout, covering the full request/response exchange. Defaults to 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = DefaultTimeout;

    /// <summary>
    /// Maximum retries for transient failures (HTTP 500/502/503/504, timeouts, connection
    /// errors) on idempotent GET requests. Defaults to 2 (three total attempts); 0 disables
    /// retries. Writes (POST/PATCH) are never retried regardless of this value — the API has
    /// no idempotency keys, so a request that was applied but whose response was lost must
    /// not be repeated.
    /// </summary>
    public int MaxRetries { get; set; } = DefaultMaxRetries;
}