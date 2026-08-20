namespace Pagr.Sdk.Exceptions;

/// <summary>
/// The request never produced an HTTP response.
/// </summary>
/// <remarks>
/// Raised when the transport fails before or during the request — connection refused, DNS
/// failure, TLS handshake error, or connection reset. Wraps the underlying
/// <see cref="System.Net.Http.HttpRequestException"/> (available via
/// <see cref="Exception.InnerException"/>). <see cref="PagrApiException.StatusCode"/> and
/// <see cref="PagrApiException.Code"/> are always <see langword="null"/>.
/// </remarks>
public sealed class PagrConnectionException : PagrApiException
{
    /// <summary>Initialises a new <see cref="PagrConnectionException"/>.</summary>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="innerException">The underlying connection exception, if any.</param>
    public PagrConnectionException(string message, Exception? innerException = null)
        : base(message, statusCode: null, code: null, innerException) { }
}
