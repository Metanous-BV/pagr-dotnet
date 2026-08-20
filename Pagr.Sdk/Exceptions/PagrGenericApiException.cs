namespace Pagr.Sdk.Exceptions;

/// <summary>
/// A Pagr failure that has no more specific exception type.
/// </summary>
/// <remarks>
/// Thrown for an HTTP status the SDK does not map to a dedicated subclass (anything other
/// than 401/403/404/413/422/429 — most often a 5xx), and for the handful of SDK-level
/// failures that are neither a transport error nor a decode error. It exists so
/// <see cref="PagrApiException"/> can stay <see langword="abstract"/>: the catch-all
/// supertype is never also a concrete thrown type, so
/// <c>catch (PagrGenericApiException)</c> means exactly "an unmapped failure" and never
/// silently swallows a subclass a caller meant to handle separately.
/// </remarks>
public sealed class PagrGenericApiException : PagrApiException
{
    /// <summary>Initialises a new <see cref="PagrGenericApiException"/>.</summary>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="statusCode">The originating HTTP status code, if any.</param>
    /// <param name="code">The API error code from the response envelope, if any.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public PagrGenericApiException(
        string message,
        int? statusCode = null,
        string? code = null,
        Exception? innerException = null)
        : base(message, statusCode, code, innerException) { }
}
