namespace Pagr.Sdk.Exceptions;

/// <summary>
/// 429 — too many requests; the organisation exceeded its rate limit for this endpoint
/// category over the current sliding 60-second window. Never retried by the SDK: a rate
/// limit reflects the caller's own request volume, so it surfaces here for the caller to
/// handle rather than being silently retried.
/// </summary>
public sealed class PagrRateLimitException : PagrApiException
{
    /// <summary>
    /// The number of seconds the server asked the caller to wait before retrying, parsed
    /// from the <c>Retry-After</c> response header when it carries an integer number of
    /// seconds. <see langword="null"/> when the header is absent or not an integer (e.g. an
    /// HTTP-date) — treat <see langword="null"/> as "back off using your own policy."
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Initialises a new <see cref="PagrRateLimitException"/>.</summary>
    public PagrRateLimitException(string message, int? statusCode = 429, string? code = null, TimeSpan? retryAfter = null)
        : base(message, statusCode, code)
    {
        RetryAfter = retryAfter;
    }
}