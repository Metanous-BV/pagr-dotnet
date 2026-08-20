namespace Pagr.Sdk.Exceptions;

/// <summary>
/// The request exceeded the configured timeout.
/// </summary>
/// <remarks>
/// Wraps the transport-level timeout (available via <see cref="Exception.InnerException"/>).
/// <see cref="PagrApiException.StatusCode"/> and <see cref="PagrApiException.Code"/> are
/// always <see langword="null"/>. Not thrown for caller-initiated cancellation — a
/// <see cref="CancellationToken"/> the caller cancelled propagates as a plain
/// <see cref="OperationCanceledException"/> instead.
/// </remarks>
public sealed class PagrTimeoutException : PagrApiException
{
    /// <summary>Initialises a new <see cref="PagrTimeoutException"/>.</summary>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="innerException">The underlying timeout exception, if any.</param>
    public PagrTimeoutException(string message, Exception? innerException = null)
        : base(message, statusCode: null, code: null, innerException) { }
}
