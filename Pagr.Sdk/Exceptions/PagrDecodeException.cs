namespace Pagr.Sdk.Exceptions;

/// <summary>
/// A successful HTTP response could not be parsed into the expected shape.
/// </summary>
/// <remarks>
/// Thrown when the transport received a response the SDK accepted at the status-code level,
/// but whose body was not the JSON/structure a method needs — a non-JSON or empty body where
/// JSON was expected, or a payload missing a field a model requires. Wraps the underlying
/// <see cref="System.Text.Json.JsonException"/> (available via <see cref="Exception.InnerException"/>)
/// so callers still only ever catch <see cref="PagrApiException"/>. When it stems from an HTTP
/// response, <see cref="PagrApiException.StatusCode"/> carries that response's status.
/// </remarks>
public sealed class PagrDecodeException : PagrApiException
{
    /// <summary>Initialises a new <see cref="PagrDecodeException"/>.</summary>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="statusCode">The originating HTTP status code, if any.</param>
    /// <param name="innerException">The underlying decode exception, if any.</param>
    public PagrDecodeException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, statusCode, code: null, innerException) { }
}
